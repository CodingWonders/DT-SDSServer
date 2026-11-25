namespace Sds.Data.Entities;

/// <summary>
/// An install image group, mapped one to one to a folder IMAGES\&lt;Name&gt; holding Res.RWM
/// and the metadata WIMs of its images.
/// </summary>
public class ImageGroup
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Group name; also the folder name. Unique, case insensitive.</summary>
    public required string Name { get; set; }

    /// <summary>Free text description (not stored on disk).</summary>
    public string? Description { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last change time (UTC).</summary>
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Images of the group.</summary>
    public List<InstallImage> Images { get; set; } = [];
}

/// <summary>
/// One install image, stored on disk as IMAGES\&lt;Group&gt;\&lt;FileName&gt; (metadata only WIM)
/// plus its resources in the group Res.RWM.
/// </summary>
public class InstallImage
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Owning group.</summary>
    public int ImageGroupId { get; set; }

    /// <summary>Navigation to the owning group.</summary>
    public ImageGroup? ImageGroup { get; set; }

    /// <summary>File name of the metadata WIM inside the group folder, for example install.wim.</summary>
    public required string FileName { get; set; }

    /// <summary>Image name shown to clients (stored in the WIM XML as well).</summary>
    public required string Name { get; set; }

    /// <summary>Image description (stored in the WIM XML as well).</summary>
    public string? Description { get; set; }

    /// <summary>Architecture: x86, amd64, arm64...</summary>
    public string Architecture { get; set; } = "unknown";

    /// <summary>Product name from the WIM XML.</summary>
    public string? ProductName { get; set; }

    /// <summary>Edition id from the WIM XML (for example Professional).</summary>
    public string? EditionId { get; set; }

    /// <summary>Installation type (Client, Server, Server Core).</summary>
    public string? InstallationType { get; set; }

    /// <summary>Product type (WinNT, ServerNT).</summary>
    public string? ProductType { get; set; }

    /// <summary>FLAGS element of the WIM XML.</summary>
    public string? Flags { get; set; }

    /// <summary>OS version major.minor.build.spbuild.</summary>
    public string? Version { get; set; }

    /// <summary>Service pack level.</summary>
    public int ServicePackLevel { get; set; }

    /// <summary>HAL name, if any.</summary>
    public string? Hal { get; set; }

    /// <summary>Languages contained in the image.</summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>Default language.</summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>Uncompressed size in bytes.</summary>
    public long TotalBytes { get; set; }

    /// <summary>File count inside the image.</summary>
    public long FileCount { get; set; }

    /// <summary>
    /// Image priority. WDS sorts images by priority (lower first), then by name.
    /// The WDS default is 500000.
    /// </summary>
    public int Priority { get; set; } = 500000;

    /// <summary>Disabled images are hidden from clients but kept on disk.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Relative path of the image unattend file, for example
    /// IMAGES\Group\install\Unattend\ImageUnattend.xml; null when not set.
    /// </summary>
    public string? UnattendFile { get; set; }

    /// <summary>Path of the source WIM the image was added from (informational).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Index inside the source WIM (informational).</summary>
    public int SourceIndex { get; set; }

    /// <summary>Lifecycle status.</summary>
    public ImageStatus Status { get; set; } = ImageStatus.Ready;

    /// <summary>Last error message when <see cref="Status"/> is Error or Missing.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last change time (UTC).</summary>
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
