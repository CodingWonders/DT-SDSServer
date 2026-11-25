using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Core.Jobs;
using Sds.Core.Storage;
using Sds.Core.Util;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Drivers;

/// <summary>
/// Driver store: imports driver packages into DRIVERS\&lt;drvname&gt;_&lt;arch&gt;_{GUID}, keeps their
/// metadata and hardware ids in the database, and manages their properties.
/// </summary>
public sealed class DriverService(
    IDbContextFactory<SdsDbContext> dbFactory,
    ShareLayout layout,
    JobService jobs,
    ILogger<DriverService> logger)
{
    // Setup classes WDS considers boot critical: storage controllers and network adapters.
    private static readonly HashSet<Guid> BootCriticalClasses =
    [
        new("4D36E97B-E325-11CE-BFC1-08002BE10318"), // SCSIAdapter
        new("4D36E96A-E325-11CE-BFC1-08002BE10318"), // HDC
        new("4D36E972-E325-11CE-BFC1-08002BE10318"), // Net
    ];

    // ------------------------------------------------------------------
    // Section: queries
    // ------------------------------------------------------------------

    /// <summary>Lists packages with optional filters.</summary>
    public async Task<List<DriverPackageDto>> ListAsync(string? architecture, string? className, string? group, string? search, bool? enabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.DriverPackages.AsNoTracking().Include(p => p.Groups).Include(p => p.HardwareIds).AsSplitQuery().AsQueryable();
        if (architecture is not null)
        {
            var arch = Identifiers.NormalizeArchitecture(architecture);
            query = query.Where(p => p.Architecture == arch);
        }

        if (className is not null)
        {
            query = query.Where(p => p.ClassName == className);
        }

        if (group is not null)
        {
            query = query.Where(p => p.Groups.Any(g => g.Name == group));
        }

        if (enabled is not null)
        {
            query = query.Where(p => p.Enabled == enabled);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => EF.Functions.Like(p.DisplayName, $"%{search}%")
                || EF.Functions.Like(p.Provider!, $"%{search}%")
                || p.HardwareIds.Any(h => EF.Functions.Like(h.HardwareId, $"%{search}%")));
        }

        var list = await query.OrderBy(p => p.DisplayName).ThenBy(p => p.Architecture).ToListAsync(ct);
        return list.Select(p => ToDto(p, includeIds: false)).ToList();
    }

    /// <summary>Returns a package with its hardware ids.</summary>
    public async Task<DriverPackageDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.DriverPackages.AsNoTracking().Include(x => x.Groups).Include(x => x.HardwareIds).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SdsNotFoundException($"Driver package {id} not found.");
        return ToDto(p, includeIds: true);
    }

    /// <summary>Changes display name or enabled state.</summary>
    public async Task<DriverPackageDto> UpdateAsync(Guid id, UpdateDriverRequest request, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.DriverPackages.Include(x => x.Groups).Include(x => x.HardwareIds).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SdsNotFoundException($"Driver package {id} not found.");
        if (request.DisplayName is not null)
        {
            p.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? throw new SdsValidationException("DisplayName cannot be empty.") : request.DisplayName.Trim();
        }

        p.Enabled = request.Enabled ?? p.Enabled;
        await db.SaveChangesAsync(ct);
        return ToDto(p, includeIds: true);
    }

    /// <summary>Deletes a package (folder and database row, including group memberships).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.DriverPackages.Include(x => x.Groups).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SdsNotFoundException($"Driver package {id} not found.");
        db.DriverPackages.Remove(p);
        await db.SaveChangesAsync(ct);

        var dir = layout.DriverDirectory(p.FolderName);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        logger.LogInformation("Deleted driver package {Folder}", p.FolderName);
    }

    // ------------------------------------------------------------------
    // Section: import
    // ------------------------------------------------------------------

    /// <summary>Queues an import from a folder, an INF file or a ZIP file.</summary>
    public async Task<Job> QueueImportAsync(DriverImportRequest request, string? requestedBy, bool deleteSourceWhenDone = false, CancellationToken ct = default)
    {
        var source = request.SourcePath ?? throw new SdsValidationException("SourcePath is required.");
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new SdsValidationException($"Source '{source}' not found.");
        }

        var archFilter = request.Architectures?.Select(a => Identifiers.NormalizeArchitecture(a)!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Fail fast on unknown driver groups.
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            foreach (var g in request.DriverGroups ?? [])
            {
                if (!await db.DriverGroups.AnyAsync(x => x.Name == g, ct))
                {
                    throw new SdsValidationException($"Driver group '{g}' not found.");
                }
            }
        }

        return await jobs.EnqueueAsync("ImportDrivers", $"Import drivers from {source}", requestedBy, async (ctx, token) =>
        {
            string? extracted = null;
            try
            {
                var root = source;
                if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // ZIP: extract to staging first.
                    extracted = Path.Combine(layout.Staging, "drivers", Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(source, extracted);
                    root = extracted;
                }

                var infs = File.Exists(root) ? [root] : Directory.GetFiles(root, "*.inf", SearchOption.AllDirectories);
                var result = new DriverImportResult([], [], []);
                for (int i = 0; i < infs.Length; i++)
                {
                    await ImportInfAsync(infs[i], archFilter, request, result, token);
                    ctx.Report((i + 1) * 100 / Math.Max(1, infs.Length));
                }

                logger.LogInformation("Driver import from {Source}: {Imported} imported, {Duplicates} duplicates, {Skipped} skipped",
                    source, result.Imported.Count, result.Duplicates.Count, result.Skipped.Count);
                return result;
            }
            finally
            {
                if (extracted is not null)
                {
                    TryDeleteDirectory(extracted);
                }

                if (deleteSourceWhenDone && File.Exists(source))
                {
                    File.Delete(source);
                }
            }
        }, ct);
    }

    private async Task ImportInfAsync(string infPath, HashSet<string>? archFilter, DriverImportRequest request, DriverImportResult result, CancellationToken ct)
    {
        InfInfo info;
        try
        {
            info = InfParser.ParseFile(infPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Skipped.Add(new SkippedInf(infPath, ex.Message));
            return;
        }

        var archs = info.ModelsByArchitecture.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key)
            .Where(a => archFilter is null || archFilter.Contains(a)).ToList();
        if (archs.Count == 0)
        {
            result.Skipped.Add(new SkippedInf(infPath, "The INF declares no device models for the requested architectures."));
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(infPath, ct)));
        var infName = Path.GetFileName(infPath);
        var infDir = Path.GetDirectoryName(infPath)!;

        foreach (var arch in archs)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Duplicate: same INF content for the same architecture already in the store.
            var existing = await db.DriverPackages.Include(p => p.Groups).Include(p => p.HardwareIds)
                .FirstOrDefaultAsync(p => p.InfHash == hash && p.Architecture == arch, ct);
            if (existing is not null)
            {
                await AddToGroupsAsync(db, existing, request.DriverGroups, ct);
                result.Duplicates.Add(ToDto(existing, includeIds: false));
                continue;
            }

            // Copy into a temporary folder inside DRIVERS, then rename: a crash never leaves a
            // half copied folder with a valid package name.
            var id = Guid.NewGuid();
            var folder = ShareLayout.DriverFolderName(infName, arch, id);
            var temp = layout.DriverDirectory(folder + ".importing");
            var final = layout.DriverDirectory(folder);
            try
            {
                var catalog = info.CatalogFor(arch);
                CopyPackageFiles(infPath, infDir, temp, info.FilesByArchitecture.GetValueOrDefault(arch) ?? [], catalog, result);
                Directory.Move(temp, final);

                var package = new DriverPackage
                {
                    Id = id,
                    FolderName = folder,
                    InfName = infName,
                    DisplayName = Path.GetFileNameWithoutExtension(infName),
                    ClassName = info.ClassName,
                    ClassGuid = info.ClassGuid,
                    Provider = info.Provider,
                    Version = info.Version,
                    DriverDate = info.DriverDate,
                    Architecture = arch,
                    CatalogFile = catalog is not null && File.Exists(Path.Combine(final, catalog)) ? catalog : null,
                    BootCritical = info.ClassGuid is Guid g && BootCriticalClasses.Contains(g),
                    Enabled = request.Enabled,
                    InfHash = hash,
                    SizeBytes = Directory.EnumerateFiles(final, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length),
                    SourcePath = infPath,
                    HardwareIds = info.ModelsByArchitecture[arch]
                        .SelectMany(m => m.HardwareIds.Select(h => (Id: h, m.Description)))
                        .DistinctBy(x => x.Id)
                        .Select(x => new DriverHardwareId { HardwareId = x.Id, Description = x.Description })
                        .ToList(),
                };

                db.DriverPackages.Add(package);
                await db.SaveChangesAsync(ct);
                await AddToGroupsAsync(db, package, request.DriverGroups, ct);
                result.Imported.Add(ToDto(package, includeIds: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
            {
                TryDeleteDirectory(temp);
                TryDeleteDirectory(final);
                result.Skipped.Add(new SkippedInf(infPath, $"{arch}: {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// Copies the INF, its catalog and the files listed in SourceDisksFiles. When the INF has
    /// no SourceDisksFiles section, the whole folder of the INF is copied instead.
    /// </summary>
    private static void CopyPackageFiles(string infPath, string infDir, string target, List<InfSourceFile> files, string? catalog, DriverImportResult result)
    {
        Directory.CreateDirectory(target);
        File.Copy(infPath, Path.Combine(target, Path.GetFileName(infPath)), overwrite: true);

        if (files.Count == 0)
        {
            CopyDirectory(infDir, target);
            return;
        }

        if (catalog is not null && File.Exists(Path.Combine(infDir, catalog)))
        {
            File.Copy(Path.Combine(infDir, catalog), Path.Combine(target, catalog), overwrite: true);
        }

        foreach (var file in files)
        {
            var from = Path.GetFullPath(Path.Combine(infDir, file.RelativePath));
            var to = Path.GetFullPath(Path.Combine(target, file.RelativePath));

            // Never follow paths that escape the package folders.
            if (!from.StartsWith(Path.GetFullPath(infDir), StringComparison.OrdinalIgnoreCase) || !to.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(new SkippedInf(infPath, $"Ignored file outside the package: {file.RelativePath}"));
                continue;
            }

            if (!File.Exists(from))
            {
                // Optional files are common (for example co-installers for other platforms).
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }

    private static async Task AddToGroupsAsync(SdsDbContext db, DriverPackage package, List<string>? groups, CancellationToken ct)
    {
        if (groups is null || groups.Count == 0)
        {
            return;
        }

        foreach (var name in groups)
        {
            var group = await db.DriverGroups.FirstAsync(g => g.Name == name, ct);
            if (!package.Groups.Any(g => g.Id == group.Id))
            {
                package.Groups.Add(group);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Registers a package folder found on disk but missing from the database (used by the
    /// reconciler). Returns null when the folder holds no usable INF.
    /// </summary>
    internal async Task<DriverPackage?> RegisterExistingFolderAsync(string folderName, CancellationToken ct)
    {
        if (!ShareLayout.TryParseDriverFolderName(folderName, out _, out var arch, out var id))
        {
            return null;
        }

        var dir = layout.DriverDirectory(folderName);
        var inf = Directory.GetFiles(dir, "*.inf").FirstOrDefault();
        if (inf is null)
        {
            return null;
        }

        var info = InfParser.ParseFile(inf);
        var models = info.ModelsByArchitecture.GetValueOrDefault(arch) ?? [];
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(inf, ct)));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.DriverPackages.AnyAsync(p => p.Id == id || (p.InfHash == hash && p.Architecture == arch), ct))
        {
            return null;
        }

        var package = new DriverPackage
        {
            Id = id,
            FolderName = folderName,
            InfName = Path.GetFileName(inf),
            DisplayName = Path.GetFileNameWithoutExtension(inf),
            ClassName = info.ClassName,
            ClassGuid = info.ClassGuid,
            Provider = info.Provider,
            Version = info.Version,
            DriverDate = info.DriverDate,
            Architecture = arch,
            CatalogFile = info.CatalogFor(arch),
            BootCritical = info.ClassGuid is Guid g && BootCriticalClasses.Contains(g),
            InfHash = hash,
            SizeBytes = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length),
            SourcePath = dir,
            HardwareIds = models.SelectMany(m => m.HardwareIds.Select(h => (Id: h, m.Description))).DistinctBy(x => x.Id)
                .Select(x => new DriverHardwareId { HardwareId = x.Id, Description = x.Description }).ToList(),
        };
        db.DriverPackages.Add(package);
        await db.SaveChangesAsync(ct);
        return package;
    }

    internal DriverPackageDto ToDto(DriverPackage p, bool includeIds) => new()
    {
        Id = p.Id,
        FolderName = p.FolderName,
        InfName = p.InfName,
        DisplayName = p.DisplayName,
        ClassName = p.ClassName,
        ClassGuid = p.ClassGuid,
        Provider = p.Provider,
        Version = p.Version,
        DriverDate = p.DriverDate,
        Architecture = p.Architecture,
        CatalogFile = p.CatalogFile,
        Signed = p.CatalogFile is not null,
        BootCritical = p.BootCritical,
        Enabled = p.Enabled,
        SizeBytes = p.SizeBytes,
        ImportedUtc = p.ImportedUtc,
        HardwareIdCount = p.HardwareIds.Count,
        Groups = p.Groups.Select(g => g.Name).OrderBy(n => n).ToList(),
        UncPath = layout.ToUnc(layout.DriverDirectory(p.FolderName)),
        HardwareIds = includeIds ? p.HardwareIds.Select(h => new HardwareIdDto(h.HardwareId, h.Description)).ToList() : null,
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
