using System.Security.AccessControl;

namespace Sds.Imaging;

/// <summary>
/// Implements the WDS install image storage format on top of wimgapi.
/// </summary>
/// <remarks>
/// <para>
/// WDS stores every install image group as one folder containing:
/// </para>
/// <list type="bullet">
///   <item><description><c>Res.RWM</c>: a WIM file without images that holds the file resources
///   (the actual file contents) of every image of the group. Identical files are stored only
///   once, which is the single instance storage WDS is known for.</description></item>
///   <item><description>One <c>&lt;name&gt;.wim</c> per image that only contains the metadata
///   resource (directory tree, security descriptors, XML data) and refers to Res.RWM for
///   the file data.</description></item>
/// </list>
/// <para>
/// Adding an image is a two step export from the source WIM: first with
/// WIM_EXPORT_ONLY_RESOURCES into Res.RWM, then with WIM_EXPORT_ONLY_METADATA into the new
/// per image file whose reference file is Res.RWM. Retrieving an image reverses the process:
/// the metadata WIM is opened with Res.RWM as reference and exported in full to a standalone
/// WIM. This class is stateless and not thread safe per group: callers must serialise writes
/// to the same group folder (see the group lock in Sds.Core).
/// </para>
/// </remarks>
public static class WdsImageFormat
{
    /// <summary>File name of the shared resource file inside every image group folder.</summary>
    public const string ResourceFileName = "Res.RWM";

    /// <summary>Lists the images of a source WIM, ESD or WDS metadata WIM.</summary>
    /// <param name="wimPath">File to inspect.</param>
    /// <param name="resourceFile">Optional Res.RWM to reference (only needed for WDS metadata WIMs).</param>
    /// <param name="temporaryPath">Scratch directory for wimgapi.</param>
    public static IReadOnlyList<WimImageInfo> ListImages(string wimPath, string? resourceFile, string temporaryPath)
    {
        using var wim = WimFile.Open(wimPath, writable: false, temporaryPath);
        if (resourceFile is not null)
        {
            wim.AddReference(resourceFile);
        }

        return wim.GetImages();
    }

    /// <summary>
    /// Adds one image of a source WIM to an image group, splitting it into Res.RWM and a
    /// metadata only WIM exactly like WDS does.
    /// </summary>
    /// <param name="sourceWim">Source WIM or ESD file (for example sources\install.wim).</param>
    /// <param name="sourceIndex">1 based index of the image to add.</param>
    /// <param name="groupDirectory">Image group folder (IMAGES\&lt;group&gt;).</param>
    /// <param name="targetFileName">File name of the metadata WIM, for example install.wim.</param>
    /// <param name="compression">Compression used when Res.RWM or the metadata WIM is created.</param>
    /// <param name="temporaryPath">Scratch directory for wimgapi.</param>
    /// <param name="newName">Optional new image name, null keeps the source name.</param>
    /// <param name="newDescription">Optional new description, null keeps the source description.</param>
    /// <param name="progress">Optional progress sink reporting 0 to 100.</param>
    /// <returns>The image information as stored in the new metadata WIM.</returns>
    public static WimImageInfo AddImage(
        string sourceWim,
        int sourceIndex,
        string groupDirectory,
        string targetFileName,
        WimCompression compression,
        string temporaryPath,
        string? newName = null,
        string? newDescription = null,
        IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(groupDirectory);
        var resourcePath = Path.Combine(groupDirectory, ResourceFileName);
        var targetPath = Path.Combine(groupDirectory, targetFileName);

        if (File.Exists(targetPath))
        {
            throw new IOException($"The image file '{targetPath}' already exists.");
        }

        // Progress: the resource export does the heavy lifting (about 95 percent of the
        // time), the metadata export is quick. Scale the two phases accordingly.
        var resourceProgress = progress is null ? null : new InlineProgress<int>(p => progress.Report(p * 95 / 100));

        using (var source = WimFile.Open(sourceWim, writable: false, temporaryPath))
        {
            if (resourceProgress is not null)
            {
                source.RegisterProgress(resourceProgress);
            }

            using var image = source.LoadImage(sourceIndex);

            // Step 1: append the file resources to Res.RWM. WIM_OPEN_ALWAYS creates the
            // resource file the first time an image is added to the group. Files already
            // present in Res.RWM (same hash) are not stored again.
            using (var resource = WimFile.Create(resourcePath, compression, temporaryPath, openIfExists: true))
            {
                image.ExportTo(resource, WimExportMode.ResourcesOnly);
            }

            // Step 2: write the metadata only WIM. The reference to Res.RWM tells wimgapi that
            // the resources the metadata points to are available, so they are not copied.
            try
            {
                using var metadata = WimFile.Create(targetPath, compression, temporaryPath);
                metadata.AddReference(resourcePath);
                image.ExportTo(metadata, WimExportMode.MetadataOnly);
            }
            catch
            {
                // Leave no half written metadata file behind. Resources already appended to
                // Res.RWM are harmless and are reclaimed by Compact.
                TryDelete(targetPath);
                throw;
            }
        }

        // Step 3 (optional): rename the image inside the metadata WIM, like the WDS
        // "Image name" and "Image description" fields of the add image wizard.
        if (newName is not null || newDescription is not null)
        {
            UpdateImageText(targetPath, resourcePath, temporaryPath, newName, newDescription);
        }

        progress?.Report(100);
        return ReadImageInfo(targetPath, resourcePath, temporaryPath);
    }

    /// <summary>
    /// Rebuilds a standalone WIM from a metadata WIM and Res.RWM (WDS "Export Image").
    /// </summary>
    /// <param name="metadataWim">Metadata only WIM inside the group folder.</param>
    /// <param name="resourceFile">Res.RWM of the same group.</param>
    /// <param name="destinationWim">Destination file to create or append to.</param>
    /// <param name="compression">Compression for a newly created destination.</param>
    /// <param name="temporaryPath">Scratch directory for wimgapi.</param>
    /// <param name="append">True to append to an existing destination WIM.</param>
    /// <param name="newName">Optional name for the exported image.</param>
    /// <param name="newDescription">Optional description for the exported image.</param>
    /// <param name="progress">Optional progress sink reporting 0 to 100.</param>
    public static WimImageInfo ExportImage(
        string metadataWim,
        string resourceFile,
        string destinationWim,
        WimCompression compression,
        string temporaryPath,
        bool append = false,
        string? newName = null,
        string? newDescription = null,
        IProgress<int>? progress = null)
    {
        int newIndex;
        using (var source = WimFile.Open(metadataWim, writable: false, temporaryPath))
        {
            // Without the reference the image has metadata but no file data to copy.
            source.AddReference(resourceFile);
            if (progress is not null)
            {
                source.RegisterProgress(progress);
            }

            using var image = source.LoadImage(1);
            using var destination = WimFile.Create(destinationWim, compression, temporaryPath, openIfExists: append);

            // A full export copies both metadata and every resource the image needs,
            // producing a self contained WIM that any tool (DISM, Setup) can apply.
            image.ExportTo(destination, WimExportMode.Full);
            newIndex = destination.ImageCount;
        }

        if (newName is not null || newDescription is not null)
        {
            using var destination = WimFile.Open(destinationWim, writable: true, temporaryPath);
            using var image = destination.LoadImage(newIndex);
            image.SetXml(WimXml.UpdateImageText(image.GetXml(), newName, newDescription));
        }

        progress?.Report(100);
        using (var check = WimFile.Open(destinationWim, writable: false, temporaryPath))
        using (var image = check.LoadImage(newIndex))
        {
            return image.GetInfo();
        }
    }

    /// <summary>Reads the image information of a WDS metadata WIM.</summary>
    public static WimImageInfo ReadImageInfo(string metadataWim, string resourceFile, string temporaryPath)
    {
        using var wim = WimFile.Open(metadataWim, writable: false, temporaryPath);
        wim.AddReference(resourceFile);

        // The XML block of the file is enough; loading the image is not required.
        var images = wim.GetImages();
        if (images.Count == 0)
        {
            throw new InvalidDataException($"'{metadataWim}' does not contain an image.");
        }

        return images[0];
    }

    /// <summary>Changes name and description stored inside a metadata WIM.</summary>
    public static void UpdateImageText(string metadataWim, string resourceFile, string temporaryPath, string? name, string? description)
    {
        using var wim = WimFile.Open(metadataWim, writable: true, temporaryPath);
        wim.AddReference(resourceFile);
        using var image = wim.LoadImage(1);
        image.SetXml(WimXml.UpdateImageText(image.GetXml(), name, description));
    }

    /// <summary>
    /// Rebuilds Res.RWM so that it only contains resources still used by the remaining
    /// metadata WIMs of the group, and rewrites those metadata WIMs against the new file.
    /// </summary>
    /// <remarks>
    /// WDS never shrinks Res.RWM when an image is deleted. This operation is the SDS
    /// equivalent of exporting every image out and adding it back. NTFS permissions on each
    /// metadata WIM (the WDS per image user filter) are preserved.
    /// </remarks>
    /// <returns>Bytes reclaimed (old size minus new size).</returns>
    public static long Compact(string groupDirectory, WimCompression compression, string temporaryPath, IProgress<int>? progress = null)
    {
        var resourcePath = Path.Combine(groupDirectory, ResourceFileName);
        if (!File.Exists(resourcePath))
        {
            return 0;
        }

        var metadataFiles = Directory.GetFiles(groupDirectory, "*.wim");
        var oldSize = new FileInfo(resourcePath).Length;

        // Everything is first written next to the originals with a .new suffix so that a
        // failure leaves the group untouched.
        var newResource = resourcePath + ".new";
        TryDelete(newResource);
        var pending = new List<(string Original, string Rebuilt)>();

        try
        {
            for (int i = 0; i < metadataFiles.Length; i++)
            {
                var original = metadataFiles[i];
                var rebuilt = original + ".new";
                TryDelete(rebuilt);

                using var source = WimFile.Open(original, writable: false, temporaryPath);
                source.AddReference(resourcePath);
                using var image = source.LoadImage(1);

                using (var resource = WimFile.Create(newResource, compression, temporaryPath, openIfExists: true))
                {
                    image.ExportTo(resource, WimExportMode.ResourcesOnly);
                }

                using (var metadata = WimFile.Create(rebuilt, compression, temporaryPath))
                {
                    metadata.AddReference(newResource);
                    image.ExportTo(metadata, WimExportMode.MetadataOnly);
                }

                pending.Add((original, rebuilt));
                progress?.Report((i + 1) * 90 / Math.Max(1, metadataFiles.Length));
            }

            if (pending.Count == 0)
            {
                // No image left: the resource file is entirely unused.
                File.Delete(resourcePath);
                progress?.Report(100);
                return oldSize;
            }

            // Swap phase: copy ACLs, then replace each file. File.Replace keeps a backup we
            // can discard afterwards.
            foreach (var (original, rebuilt) in pending)
            {
                CopyAcl(original, rebuilt);
                File.Replace(rebuilt, original, destinationBackupFileName: null);
            }

            CopyAcl(resourcePath, newResource);
            File.Replace(newResource, resourcePath, destinationBackupFileName: null);
            progress?.Report(100);
            return oldSize - new FileInfo(resourcePath).Length;
        }
        catch
        {
            TryDelete(newResource);
            foreach (var (_, rebuilt) in pending)
            {
                TryDelete(rebuilt);
            }

            throw;
        }
    }

    /// <summary>Copies the NTFS security descriptor (DACL) from one file to another.</summary>
    public static void CopyAcl(string from, string to)
    {
        var security = new FileInfo(from).GetAccessControl(AccessControlSections.Access);
        new FileInfo(to).SetAccessControl(security);
    }

    private static void TryDelete(string path)
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
            // Best effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup only.
        }
    }
}
