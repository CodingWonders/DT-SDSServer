using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Core.Drivers;
using Sds.Core.Images;
using Sds.Core.Storage;
using Sds.Data;
using Sds.Data.Entities;
using Sds.Imaging;

namespace Sds.Core.Maintenance;

/// <summary>Outcome of a reconciliation pass.</summary>
public sealed record ReconcileReport
{
    /// <summary>Image groups found on disk and added to the database.</summary>
    public List<string> GroupsImported { get; init; } = [];

    /// <summary>Image groups in the database whose folder is missing (recreated empty).</summary>
    public List<string> GroupsRecreated { get; init; } = [];

    /// <summary>Metadata WIMs found on disk and added to the database.</summary>
    public List<string> ImagesImported { get; init; } = [];

    /// <summary>Images whose files are missing (marked Missing).</summary>
    public List<string> ImagesMissing { get; init; } = [];

    /// <summary>Images that were Missing and are back (marked Ready).</summary>
    public List<string> ImagesRestored { get; init; } = [];

    /// <summary>Driver folders found on disk and registered.</summary>
    public List<string> DriversImported { get; init; } = [];

    /// <summary>Driver packages whose folder is missing.</summary>
    public List<string> DriversMissing { get; init; } = [];

    /// <summary>Folders in DRIVERS that could not be registered.</summary>
    public List<string> DriverFoldersIgnored { get; init; } = [];

    /// <summary>Errors encountered.</summary>
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Keeps the database and the share consistent. Files may be copied in or removed by hand (or
/// a database may be restored from backup); the reconciler brings both views back together.
/// </summary>
public sealed class Reconciler(
    IDbContextFactory<SdsDbContext> dbFactory,
    ShareLayout layout,
    DriverService drivers,
    ILogger<Reconciler> logger)
{
    /// <summary>Runs a full pass over IMAGES and DRIVERS.</summary>
    public async Task<ReconcileReport> RunAsync(CancellationToken ct = default)
    {
        var report = new ReconcileReport();
        await ReconcileImagesAsync(report, ct);
        await ReconcileDriversAsync(report, ct);
        logger.LogInformation(
            "Reconcile: {Groups} group(s) and {Images} image(s) imported, {Missing} image(s) missing, {Drivers} driver folder(s) registered, {DriversMissing} driver(s) missing",
            report.GroupsImported.Count, report.ImagesImported.Count, report.ImagesMissing.Count, report.DriversImported.Count, report.DriversMissing.Count);
        return report;
    }

    private async Task ReconcileImagesAsync(ReconcileReport report, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.ImageGroups.Include(g => g.Images).ToListAsync(ct);

        // Step 1: folders on disk unknown to the database become groups.
        foreach (var dir in Directory.GetDirectories(layout.Images))
        {
            var name = Path.GetFileName(dir);
            if (!groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var group = new ImageGroup { Name = name };
                db.ImageGroups.Add(group);
                groups.Add(group);
                report.GroupsImported.Add(name);
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var group in groups)
        {
            var dir = layout.GroupDirectory(group.Name);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                report.GroupsRecreated.Add(group.Name);
            }

            var rwm = layout.ResourceFile(group.Name);

            // Step 2: status of known images.
            foreach (var image in group.Images)
            {
                var present = File.Exists(layout.ImageFile(group.Name, image.FileName)) && File.Exists(rwm);
                if (!present && image.Status != ImageStatus.Missing)
                {
                    image.Status = ImageStatus.Missing;
                    image.StatusMessage = "The metadata WIM or Res.RWM is missing.";
                    report.ImagesMissing.Add($"{group.Name}\\{image.FileName}");
                }
                else if (present && image.Status == ImageStatus.Missing)
                {
                    image.Status = ImageStatus.Ready;
                    image.StatusMessage = null;
                    report.ImagesRestored.Add($"{group.Name}\\{image.FileName}");
                }
            }

            // Step 3: metadata WIMs on disk unknown to the database become images.
            if (!File.Exists(rwm))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.wim"))
            {
                var fileName = Path.GetFileName(file);
                if (group.Images.Any(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    var info = WdsImageFormat.ReadImageInfo(file, rwm, layout.WimTemp);
                    var image = new InstallImage
                    {
                        ImageGroupId = group.Id,
                        FileName = fileName,
                        Name = string.IsNullOrEmpty(info.Name) ? Path.GetFileNameWithoutExtension(fileName) : info.Name,
                        SourcePath = file,
                        SourceIndex = 1,
                    };
                    ImageService.Apply(image, info);
                    if (File.Exists(layout.ImageUnattendFile(group.Name, fileName)))
                    {
                        image.UnattendFile = Path.GetRelativePath(dir, layout.ImageUnattendFile(group.Name, fileName));
                    }

                    db.Images.Add(image);
                    report.ImagesImported.Add($"{group.Name}\\{fileName}");
                }
                catch (Exception ex) when (ex is WimException or IOException or InvalidDataException or System.Xml.XmlException)
                {
                    report.Errors.Add($"{group.Name}\\{fileName}: {ex.Message}");
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ReconcileDriversAsync(ReconcileReport report, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var packages = await db.DriverPackages.AsNoTracking().Select(p => p.FolderName).ToListAsync(ct);
        var known = packages.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in packages.Where(f => !Directory.Exists(layout.DriverDirectory(f))))
        {
            report.DriversMissing.Add(folder);
        }

        foreach (var dir in Directory.GetDirectories(layout.Drivers))
        {
            var folder = Path.GetFileName(dir);
            if (known.Contains(folder))
            {
                continue;
            }

            if (folder.EndsWith(".importing", StringComparison.OrdinalIgnoreCase))
            {
                // Leftover of an interrupted import.
                Directory.Delete(dir, recursive: true);
                continue;
            }

            try
            {
                var registered = await drivers.RegisterExistingFolderAsync(folder, ct);
                (registered is null ? report.DriverFoldersIgnored : report.DriversImported).Add(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
            {
                report.Errors.Add($"{folder}: {ex.Message}");
            }
        }
    }
}
