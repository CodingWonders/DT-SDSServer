using System.Collections.Concurrent;
using System.Security.Principal;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sds.Core.Jobs;
using Sds.Core.Security;
using Sds.Core.Storage;
using Sds.Data;
using Sds.Data.Entities;
using Sds.Imaging;

namespace Sds.Core.Images;

/// <summary>
/// Manages install image groups and install images, reproducing the WDS install image
/// features: add (split into Res.RWM plus metadata WIM), export (merge back), copy, move,
/// replace, delete, properties, priority, enable/disable, image unattend, user filters
/// (NTFS ACLs) and Res.RWM compaction.
/// </summary>
public sealed class ImageService(
    IDbContextFactory<SdsDbContext> dbFactory,
    ShareLayout layout,
    JobService jobs,
    IOptions<SdsOptions> options,
    ILogger<ImageService> logger)
{
    // One writer per group: Res.RWM and the metadata WIMs of a group must never be modified
    // by two operations at once. Keys are upper case group names.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GroupLocks = new(StringComparer.OrdinalIgnoreCase);

    private WimCompression Compression => options.Value.Compression;

    // ------------------------------------------------------------------
    // Section: image groups
    // ------------------------------------------------------------------

    /// <summary>Lists image groups.</summary>
    public async Task<List<ImageGroupDto>> ListGroupsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.ImageGroups.AsNoTracking().Include(g => g.Images).OrderBy(g => g.Name).ToListAsync(ct);
        return groups.Select(ToDto).ToList();
    }

    /// <summary>Returns one image group.</summary>
    public async Task<ImageGroupDto> GetGroupAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.ImageGroups.AsNoTracking().Include(g => g.Images).FirstOrDefaultAsync(g => g.Name == name, ct)
            ?? throw new SdsNotFoundException($"Image group '{name}' not found.");
        return ToDto(group);
    }

    /// <summary>Creates an image group and its folder.</summary>
    public async Task<ImageGroupDto> CreateGroupAsync(CreateImageGroupRequest request, CancellationToken ct = default)
    {
        var name = ShareLayout.ValidateName(request.Name, "Group name");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.ImageGroups.AnyAsync(g => g.Name == name, ct))
        {
            throw new SdsConflictException($"Image group '{name}' already exists.");
        }

        // The folder inherits the IMAGES permissions; administrators may restrict it later
        // through the ACL endpoint (WDS group level user filter).
        Directory.CreateDirectory(layout.GroupDirectory(name));
        var group = new ImageGroup { Name = name, Description = request.Description };
        db.ImageGroups.Add(group);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created image group {Group}", name);
        return ToDto(group);
    }

    /// <summary>Renames a group (folder move) or changes its description.</summary>
    public async Task<ImageGroupDto> UpdateGroupAsync(string name, UpdateImageGroupRequest request, CancellationToken ct = default)
    {
        using var _ = await LockGroupAsync(name, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.ImageGroups.Include(g => g.Images).FirstOrDefaultAsync(g => g.Name == name, ct)
            ?? throw new SdsNotFoundException($"Image group '{name}' not found.");

        if (request.Name is not null && !string.Equals(request.Name, group.Name, StringComparison.Ordinal))
        {
            var newName = ShareLayout.ValidateName(request.Name, "Group name");
            if (!string.Equals(newName, group.Name, StringComparison.OrdinalIgnoreCase) && await db.ImageGroups.AnyAsync(g => g.Name == newName, ct))
            {
                throw new SdsConflictException($"Image group '{newName}' already exists.");
            }

            // Lock the new name too, unless only the case changes (same lock, would deadlock).
            using var __ = string.Equals(newName, group.Name, StringComparison.OrdinalIgnoreCase)
                ? null
                : await LockGroupAsync(newName, ct);

            // Directory.Move cannot change only the case of a name; go through a temp name.
            var from = layout.GroupDirectory(group.Name);
            var to = layout.GroupDirectory(newName);
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                var temp = from + ".renaming";
                Directory.Move(from, temp);
                Directory.Move(temp, to);
            }
            else
            {
                Directory.Move(from, to);
            }

            group.Name = newName;
        }

        if (request.Description is not null)
        {
            group.Description = request.Description;
        }

        group.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    /// <summary>Deletes a group. Fails when it still contains images unless <paramref name="force"/> is set.</summary>
    public async Task DeleteGroupAsync(string name, bool force, CancellationToken ct = default)
    {
        using var _ = await LockGroupAsync(name, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.ImageGroups.Include(g => g.Images).FirstOrDefaultAsync(g => g.Name == name, ct)
            ?? throw new SdsNotFoundException($"Image group '{name}' not found.");

        if (group.Images.Count > 0 && !force)
        {
            throw new SdsConflictException($"Image group '{name}' contains {group.Images.Count} image(s). Delete them first or use force.");
        }

        // Database first, then disk: a leftover folder is picked up again by the reconciler
        // and is easy to remove, while a database row without a folder would break clients.
        db.ImageGroups.Remove(group);
        await db.SaveChangesAsync(ct);

        var dir = layout.GroupDirectory(group.Name);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        logger.LogInformation("Deleted image group {Group}", name);
    }

    /// <summary>Returns the NTFS ACL of a group folder.</summary>
    public async Task<AclDto> GetGroupAclAsync(string name, CancellationToken ct = default)
    {
        var group = await GetGroupAsync(name, ct);
        return FileAclService.Get(layout.GroupDirectory(group.Name));
    }

    /// <summary>Sets the NTFS ACL of a group folder (applies to every image of the group).</summary>
    public async Task<AclDto> SetGroupAclAsync(string name, AclDto acl, CancellationToken ct = default)
    {
        var group = await GetGroupAsync(name, ct);
        var dir = layout.GroupDirectory(group.Name);
        FileAclService.Set(dir, acl);
        return FileAclService.Get(dir);
    }

    // ------------------------------------------------------------------
    // Section: listing and properties
    // ------------------------------------------------------------------

    /// <summary>Lists images, optionally of one group, sorted like WDS (priority, then name).</summary>
    public async Task<List<ImageDto>> ListImagesAsync(string? groupName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Images.AsNoTracking().Include(i => i.ImageGroup).AsQueryable();
        if (groupName is not null)
        {
            if (!await db.ImageGroups.AnyAsync(g => g.Name == groupName, ct))
            {
                throw new SdsNotFoundException($"Image group '{groupName}' not found.");
            }

            query = query.Where(i => i.ImageGroup!.Name == groupName);
        }

        var images = await query.ToListAsync(ct);
        return images.OrderBy(i => i.Priority).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();
    }

    /// <summary>Returns one image.</summary>
    public async Task<ImageDto> GetImageAsync(int id, CancellationToken ct = default) => ToDto(await LoadImageAsync(id, ct));

    /// <summary>
    /// Updates image properties. Name and description are also written into the metadata WIM
    /// so tools reading the WIM directly see the same values (WDS does the same).
    /// </summary>
    public async Task<ImageDto> UpdateImageAsync(int id, UpdateImageRequest request, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        using var _ = await LockGroupAsync(image.ImageGroup!.Name, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Images.Include(i => i.ImageGroup).FirstAsync(i => i.Id == id, ct);

        if (request.Name is not null || request.Description is not null)
        {
            if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            {
                throw new SdsValidationException("Image name cannot be empty.");
            }

            var group = entity.ImageGroup!.Name;
            await Task.Run(() => WdsImageFormat.UpdateImageText(
                layout.ImageFile(group, entity.FileName), layout.ResourceFile(group), layout.WimTemp, request.Name, request.Description), ct);
            entity.Name = request.Name ?? entity.Name;
            entity.Description = request.Description ?? entity.Description;
        }

        entity.Enabled = request.Enabled ?? entity.Enabled;
        entity.Priority = request.Priority ?? entity.Priority;
        entity.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    /// <summary>Returns the NTFS ACL of an image file (WDS image user filter).</summary>
    public async Task<AclDto> GetImageAclAsync(int id, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        return FileAclService.Get(layout.ImageFile(image.ImageGroup!.Name, image.FileName));
    }

    /// <summary>Sets the NTFS ACL of an image file (WDS image user filter).</summary>
    public async Task<AclDto> SetImageAclAsync(int id, AclDto acl, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        var path = layout.ImageFile(image.ImageGroup!.Name, image.FileName);
        FileAclService.Set(path, acl);
        return FileAclService.Get(path);
    }

    /// <summary>Lists the images contained in a source WIM before adding them.</summary>
    public Task<IReadOnlyList<WimImageInfo>> InspectSourceAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new SdsValidationException($"Source file '{sourcePath}' not found.");
        }

        return Task.Run(() => WdsImageFormat.ListImages(sourcePath, null, layout.WimTemp), ct);
    }

    // ------------------------------------------------------------------
    // Section: add image (WDS split into Res.RWM and metadata WIM)
    // ------------------------------------------------------------------

    /// <summary>
    /// Queues the addition of one or more images of a source WIM to a group.
    /// </summary>
    /// <param name="groupName">Target group (must exist).</param>
    /// <param name="request">Options.</param>
    /// <param name="requestedBy">User name for the job record.</param>
    /// <param name="deleteSourceWhenDone">True for uploaded files living in staging.</param>
    /// <param name="ct">Cancellation of the request (not of the job).</param>
    public async Task<Job> QueueAddImagesAsync(string groupName, AddImageRequest request, string? requestedBy, bool deleteSourceWhenDone = false, CancellationToken ct = default)
    {
        var source = request.SourcePath ?? throw new SdsValidationException("SourcePath is required.");
        if (!File.Exists(source))
        {
            throw new SdsValidationException($"Source file '{source}' not found.");
        }

        var group = await GetGroupAsync(groupName, ct);
        if (request.FileName is not null)
        {
            ShareLayout.ValidateName(request.FileName, "File name");
        }

        if (request.UnattendXml is not null)
        {
            ValidateXml(request.UnattendXml);
        }

        // Read the source up front so bad files and bad indexes fail the request, not the job.
        var available = await InspectSourceAsync(source, ct);
        var indexes = request.Indexes is { Count: > 0 } ? request.Indexes.Distinct().ToList() : available.Select(i => i.Index).ToList();
        var missing = indexes.Where(i => available.All(a => a.Index != i)).ToList();
        if (missing.Count > 0)
        {
            throw new SdsValidationException($"Source does not contain image index(es) {string.Join(", ", missing)}.");
        }

        if (indexes.Count > 1 && (request.Name is not null || request.Description is not null))
        {
            throw new SdsValidationException("Name and Description can only be set when adding a single image.");
        }

        return await jobs.EnqueueAsync("AddImage", $"Add {indexes.Count} image(s) from {Path.GetFileName(source)} to {group.Name}", requestedBy, async (ctx, token) =>
        {
            try
            {
                var added = new List<ImageDto>();
                for (int n = 0; n < indexes.Count; n++)
                {
                    int slice = n;
                    var progress = new InlineProgress<int>(p => ctx.Report((slice * 100 + p) / indexes.Count));
                    added.Add(await AddSingleImageAsync(group.Name, source, indexes[n], request, progress, token));
                }

                return added;
            }
            finally
            {
                if (deleteSourceWhenDone)
                {
                    TryDeleteFile(source);
                }
            }
        }, ct);
    }

    private async Task<ImageDto> AddSingleImageAsync(string groupName, string source, int index, AddImageRequest request, IProgress<int> progress, CancellationToken ct)
    {
        using var _ = await LockGroupAsync(groupName, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.ImageGroups.FirstOrDefaultAsync(g => g.Name == groupName, ct)
            ?? throw new SdsNotFoundException($"Image group '{groupName}' was deleted.");

        // Pick the file name exactly like WDS: install.wim, then install-(2).wim, install-(3).wim...
        var baseName = Path.GetFileNameWithoutExtension(request.FileName ?? Path.GetFileName(source));
        var fileName = UniqueFileName(layout.GroupDirectory(group.Name), baseName);

        var info = WdsImageFormat.AddImage(source, index, layout.GroupDirectory(group.Name), fileName, Compression, layout.WimTemp,
            request.Name, request.Description, progress);

        var image = new InstallImage
        {
            ImageGroupId = group.Id,
            FileName = fileName,
            Name = string.IsNullOrEmpty(info.Name) ? Path.GetFileNameWithoutExtension(fileName) : info.Name,
            Priority = request.Priority ?? 500000,
            Enabled = request.Enabled,
            SourcePath = source,
            SourceIndex = index,
        };
        Apply(image, info);

        try
        {
            db.Images.Add(image);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Keep disk and database consistent: remove the metadata WIM we just wrote.
            TryDeleteFile(layout.ImageFile(group.Name, fileName));
            throw;
        }

        if (request.UnattendXml is not null)
        {
            await WriteUnattendAsync(db, image, group.Name, request.UnattendXml, ct);
        }

        logger.LogInformation("Added image {Name} ({File}) to group {Group}", image.Name, fileName, group.Name);
        image.ImageGroup = group;
        return ToDto(image);
    }

    // ------------------------------------------------------------------
    // Section: export, copy, move, replace
    // ------------------------------------------------------------------

    /// <summary>
    /// Queues an export: the metadata WIM is merged with Res.RWM into a standalone WIM
    /// (WDS "Export Image").
    /// </summary>
    public async Task<Job> QueueExportAsync(int id, ExportImageRequest request, string? requestedBy, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        var group = image.ImageGroup!.Name;
        var staged = request.DestinationPath is null;
        var destination = request.DestinationPath ?? Path.Combine(layout.ExportStaging, $"{Guid.NewGuid():N}", image.FileName);

        if (!staged && File.Exists(destination) && !request.Append)
        {
            throw new SdsConflictException($"Destination '{destination}' already exists. Set Append to add the image to it.");
        }

        return await jobs.EnqueueAsync("ExportImage", $"Export {image.Name} from {group}", requestedBy, async (ctx, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // Readers do not need the group lock for consistency of the source files, but a
            // concurrent compaction would replace Res.RWM underneath us, so take it.
            using var _ = await LockGroupAsync(group, token);
            var info = WdsImageFormat.ExportImage(layout.ImageFile(group, image.FileName), layout.ResourceFile(group), destination,
                Compression, layout.WimTemp, request.Append, request.Name, request.Description, ctx.Progress);
            return new ExportResult(destination, new FileInfo(destination).Length, info.Index, info.Name, staged);
        }, ct);
    }

    /// <summary>Queues a copy (or move) of an image to another group.</summary>
    public async Task<Job> QueueCopyAsync(int id, CopyImageRequest request, bool move, string? requestedBy, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        var sourceGroup = image.ImageGroup!.Name;
        var target = await GetGroupAsync(request.TargetGroup, ct);
        if (string.Equals(target.Name, sourceGroup, StringComparison.OrdinalIgnoreCase) && move)
        {
            throw new SdsValidationException("Source and target groups are the same.");
        }

        return await jobs.EnqueueAsync(move ? "MoveImage" : "CopyImage", $"{(move ? "Move" : "Copy")} {image.Name} from {sourceGroup} to {target.Name}", requestedBy, async (ctx, token) =>
        {
            // Step 1: rebuild a standalone WIM in staging (the WDS export path).
            var temp = Path.Combine(layout.Staging, "copy", Guid.NewGuid().ToString("N"), image.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            try
            {
                using (await LockGroupAsync(sourceGroup, token))
                {
                    WdsImageFormat.ExportImage(layout.ImageFile(sourceGroup, image.FileName), layout.ResourceFile(sourceGroup), temp,
                        Compression, layout.WimTemp, progress: new InlineProgress<int>(p => ctx.Report(p / 2)));
                }

                // Step 2: add it to the target group (split into the target Res.RWM).
                var unattend = image.UnattendFile is null ? null : await File.ReadAllTextAsync(layout.ImageUnattendFile(sourceGroup, image.FileName), token);
                var added = await AddSingleImageAsync(target.Name, temp, 1, new AddImageRequest
                {
                    FileName = request.FileName ?? image.FileName,
                    Name = request.Name ?? image.Name,
                    Description = request.Description ?? image.Description,
                    Priority = image.Priority,
                    Enabled = image.Enabled,
                    UnattendXml = unattend,
                }, new InlineProgress<int>(p => ctx.Report(50 + p / 2)), token);

                // Step 3 (move only): delete the original.
                if (move)
                {
                    await DeleteImageAsync(id, token);
                }

                return added;
            }
            finally
            {
                TryDeleteDirectory(Path.GetDirectoryName(temp)!);
            }
        }, ct);
    }

    /// <summary>
    /// Queues a replacement of the image content by another WIM image, keeping the id, file
    /// name, priority, unattend and permissions (WDS "Replace Image").
    /// </summary>
    public async Task<Job> QueueReplaceAsync(int id, ReplaceImageRequest request, string? requestedBy, bool deleteSourceWhenDone = false, CancellationToken ct = default)
    {
        var source = request.SourcePath ?? throw new SdsValidationException("SourcePath is required.");
        var image = await LoadImageAsync(id, ct);
        var available = await InspectSourceAsync(source, ct);
        if (available.All(a => a.Index != request.Index))
        {
            throw new SdsValidationException($"Source does not contain image index {request.Index}.");
        }

        var group = image.ImageGroup!.Name;
        return await jobs.EnqueueAsync("ReplaceImage", $"Replace {image.Name} in {group}", requestedBy, async (ctx, token) =>
        {
            try
            {
                using var _ = await LockGroupAsync(group, token);
                var dir = layout.GroupDirectory(group);
                var finalPath = layout.ImageFile(group, image.FileName);
                var tempName = Path.GetFileNameWithoutExtension(image.FileName) + ".replacing.wim";
                var tempPath = Path.Combine(dir, tempName);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                // Add the new content under a temporary name, then swap it in, keeping the
                // ACL (user filter) of the original file.
                var info = WdsImageFormat.AddImage(source, request.Index, dir, tempName, Compression, layout.WimTemp, progress: ctx.Progress);
                WdsImageFormat.CopyAcl(finalPath, tempPath);
                File.Replace(tempPath, finalPath, destinationBackupFileName: null);

                await using var db = await dbFactory.CreateDbContextAsync(token);
                var entity = await db.Images.FirstAsync(i => i.Id == id, token);
                Apply(entity, info);
                entity.Name = string.IsNullOrEmpty(info.Name) ? entity.Name : info.Name;
                entity.SourcePath = source;
                entity.SourceIndex = request.Index;
                entity.Status = ImageStatus.Ready;
                entity.StatusMessage = null;
                entity.ModifiedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(token);
                return ToDto(await LoadImageAsync(id, token));
            }
            finally
            {
                if (deleteSourceWhenDone)
                {
                    TryDeleteFile(source);
                }
            }
        }, ct);
    }

    /// <summary>
    /// Deletes an image: metadata WIM, unattend folder and database row. When the last image
    /// of a group is removed, Res.RWM is deleted too since nothing references it anymore.
    /// </summary>
    public async Task DeleteImageAsync(int id, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        var group = image.ImageGroup!.Name;
        using var _ = await LockGroupAsync(group, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Images.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new SdsNotFoundException($"Image {id} not found.");
        db.Images.Remove(entity);
        await db.SaveChangesAsync(ct);

        TryDeleteFile(layout.ImageFile(group, image.FileName));
        TryDeleteDirectory(Path.Combine(layout.GroupDirectory(group), Path.GetFileNameWithoutExtension(image.FileName)));

        if (!await db.Images.AnyAsync(i => i.ImageGroupId == image.ImageGroupId, ct))
        {
            TryDeleteFile(layout.ResourceFile(group));
        }

        logger.LogInformation("Deleted image {Name} ({File}) from {Group}", image.Name, image.FileName, group);
    }

    /// <summary>Queues a Res.RWM compaction for a group.</summary>
    public async Task<Job> QueueCompactAsync(string groupName, string? requestedBy, CancellationToken ct = default)
    {
        var group = await GetGroupAsync(groupName, ct);
        return await jobs.EnqueueAsync("CompactGroup", $"Compact Res.RWM of {group.Name}", requestedBy, async (ctx, token) =>
        {
            using var _ = await LockGroupAsync(group.Name, token);
            var reclaimed = WdsImageFormat.Compact(layout.GroupDirectory(group.Name), Compression, layout.WimTemp, ctx.Progress);
            var rwm = layout.ResourceFile(group.Name);
            return new CompactResult(group.Name, reclaimed, File.Exists(rwm) ? new FileInfo(rwm).Length : 0);
        }, ct);
    }

    // ------------------------------------------------------------------
    // Section: image unattend (IMAGES\Group\Image\Unattend\ImageUnattend.xml)
    // ------------------------------------------------------------------

    /// <summary>Returns the image unattend XML, or null when none is set.</summary>
    public async Task<string?> GetUnattendAsync(int id, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        if (image.UnattendFile is null)
        {
            return null;
        }

        var path = layout.ImageUnattendFile(image.ImageGroup!.Name, image.FileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    /// <summary>Sets (or with null, removes) the image unattend file.</summary>
    public async Task SetUnattendAsync(int id, string? xml, CancellationToken ct = default)
    {
        var image = await LoadImageAsync(id, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Images.FirstAsync(i => i.Id == id, ct);
        var group = image.ImageGroup!.Name;

        if (xml is null)
        {
            TryDeleteDirectory(Path.GetDirectoryName(layout.ImageUnattendFile(group, image.FileName))!);
            entity.UnattendFile = null;
            entity.ModifiedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        ValidateXml(xml);
        await WriteUnattendAsync(db, entity, group, xml, ct);
    }

    private async Task WriteUnattendAsync(SdsDbContext db, InstallImage entity, string group, string xml, CancellationToken ct)
    {
        var path = layout.ImageUnattendFile(group, entity.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, xml, ct);

        // Stored relative to the group folder so renaming the group keeps it valid.
        entity.UnattendFile = Path.GetRelativePath(layout.GroupDirectory(group), path);
        entity.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------
    // Section: client view
    // ------------------------------------------------------------------

    /// <summary>
    /// Images a deployment client may install: enabled, ready, matching the architecture and
    /// readable by the caller according to the file ACL (WDS user filter). Sorted by priority
    /// then name, like the WDS client image list.
    /// </summary>
    public async Task<List<ClientImageDto>> ListForClientAsync(WindowsIdentity? identity, string? architecture, string? group, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Images.AsNoTracking().Include(i => i.ImageGroup).Where(i => i.Enabled && i.Status == ImageStatus.Ready);
        if (architecture is not null)
        {
            query = query.Where(i => i.Architecture == architecture);
        }

        if (group is not null)
        {
            query = query.Where(i => i.ImageGroup!.Name == group);
        }

        var images = await query.ToListAsync(ct);
        return images
            .Where(i => identity is null || FileAclService.CanRead(identity, layout.ImageFile(i.ImageGroup!.Name, i.FileName)))
            .OrderBy(i => i.Priority).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => new ClientImageDto(i.Id, i.ImageGroup!.Name, i.Name, i.Description, i.Architecture, i.Languages, i.DefaultLanguage, i.Version, i.TotalBytes,
                layout.ToUnc(layout.ImageFile(i.ImageGroup.Name, i.FileName)),
                layout.ToUnc(layout.ResourceFile(i.ImageGroup.Name)),
                i.UnattendFile is null ? null : layout.ToUnc(layout.ImageUnattendFile(i.ImageGroup.Name, i.FileName)),
                i.FileName,
                i.Priority,
                i.ModifiedUtc))
            .ToList();
    }

    // ------------------------------------------------------------------
    // Section: helpers
    // ------------------------------------------------------------------

    /// <summary>Acquires the write lock of a group. Dispose the result to release it.</summary>
    internal static async Task<IDisposable> LockGroupAsync(string group, CancellationToken ct)
    {
        var semaphore = GroupLocks.GetOrAdd(group, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private async Task<InstallImage> LoadImageAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Images.AsNoTracking().Include(i => i.ImageGroup).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new SdsNotFoundException($"Image {id} not found.");
    }

    /// <summary>Copies the WIM XML derived properties onto an entity.</summary>
    internal static void Apply(InstallImage image, WimImageInfo info)
    {
        image.Description = info.Description ?? image.Description;
        image.Architecture = info.Architecture;
        image.ProductName = info.ProductName;
        image.EditionId = info.EditionId;
        image.InstallationType = info.InstallationType;
        image.ProductType = info.ProductType;
        image.Flags = info.Flags;
        image.Version = info.Version;
        image.ServicePackLevel = info.ServicePackLevel;
        image.Hal = info.Hal;
        image.Languages = info.Languages.ToList();
        image.DefaultLanguage = info.DefaultLanguage;
        image.TotalBytes = info.TotalBytes;
        image.FileCount = info.FileCount;
    }

    /// <summary>WDS naming: name.wim, name-(2).wim, name-(3).wim...</summary>
    internal static string UniqueFileName(string directory, string baseName)
    {
        baseName = ShareLayout.SanitizeFileName(baseName);
        var candidate = baseName + ".wim";
        for (int n = 2; File.Exists(Path.Combine(directory, candidate)); n++)
        {
            candidate = $"{baseName}-({n}).wim";
        }

        return candidate;
    }

    private ImageGroupDto ToDto(ImageGroup g)
    {
        var rwm = layout.ResourceFile(g.Name);
        return new ImageGroupDto(g.Id, g.Name, g.Description, g.Images.Count, File.Exists(rwm) ? new FileInfo(rwm).Length : 0,
            layout.ToUnc(layout.GroupDirectory(g.Name)), g.CreatedUtc, g.ModifiedUtc);
    }

    private ImageDto ToDto(InstallImage i)
    {
        var group = i.ImageGroup!.Name;
        return new ImageDto
        {
            Id = i.Id,
            GroupId = i.ImageGroupId,
            GroupName = group,
            FileName = i.FileName,
            Name = i.Name,
            Description = i.Description,
            Architecture = i.Architecture,
            ProductName = i.ProductName,
            EditionId = i.EditionId,
            InstallationType = i.InstallationType,
            ProductType = i.ProductType,
            Flags = i.Flags,
            Version = i.Version,
            ServicePackLevel = i.ServicePackLevel,
            Hal = i.Hal,
            Languages = i.Languages,
            DefaultLanguage = i.DefaultLanguage,
            TotalBytes = i.TotalBytes,
            FileCount = i.FileCount,
            Priority = i.Priority,
            Enabled = i.Enabled,
            HasUnattend = i.UnattendFile is not null,
            Status = i.Status,
            StatusMessage = i.StatusMessage,
            MetadataUnc = layout.ToUnc(layout.ImageFile(group, i.FileName)),
            ResourceUnc = layout.ToUnc(layout.ResourceFile(group)),
            UnattendUnc = i.UnattendFile is null ? null : layout.ToUnc(layout.ImageUnattendFile(group, i.FileName)),
            CreatedUtc = i.CreatedUtc,
            ModifiedUtc = i.ModifiedUtc,
        };
    }

    /// <summary>Rejects XML that is not well formed (unattend files are parsed by Setup).</summary>
    internal static void ValidateXml(string xml)
    {
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            doc.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new SdsValidationException($"The unattend file is not valid XML: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
