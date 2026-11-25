namespace Sds.Imaging;

/// <summary>
/// Description of one image inside a WIM file, parsed from the XML data block that
/// wimgapi exposes through WIMGetImageInformation.
/// </summary>
public sealed record WimImageInfo
{
    /// <summary>1 based index of the image inside its WIM file.</summary>
    public int Index { get; init; }

    /// <summary>Image name (NAME element). WDS displays this name to clients.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Image description (DESCRIPTION element).</summary>
    public string? Description { get; init; }

    /// <summary>Optional display name (DISPLAYNAME element).</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional display description (DISPLAYDESCRIPTION element).</summary>
    public string? DisplayDescription { get; init; }

    /// <summary>Edition flags string (FLAGS element), for example "ServerDatacenter".</summary>
    public string? Flags { get; init; }

    /// <summary>Processor architecture normalised to x86, amd64, arm, ia64 or arm64.</summary>
    public string Architecture { get; init; } = "unknown";

    /// <summary>Product name, for example "Microsoft Windows Operating System".</summary>
    public string? ProductName { get; init; }

    /// <summary>Edition identifier, for example "Professional" or "ServerStandard".</summary>
    public string? EditionId { get; init; }

    /// <summary>Installation type, for example "Client", "Server" or "Server Core".</summary>
    public string? InstallationType { get; init; }

    /// <summary>Product type, for example "WinNT" or "ServerNT".</summary>
    public string? ProductType { get; init; }

    /// <summary>HAL name reported in the image, if present.</summary>
    public string? Hal { get; init; }

    /// <summary>All languages contained in the image.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Default language of the image.</summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>Operating system version as major.minor.build.spbuild.</summary>
    public string? Version { get; init; }

    /// <summary>Service pack level.</summary>
    public int ServicePackLevel { get; init; }

    /// <summary>Uncompressed size of the image in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Number of directories in the image.</summary>
    public long DirectoryCount { get; init; }

    /// <summary>Number of files in the image.</summary>
    public long FileCount { get; init; }

    /// <summary>Creation time recorded in the image (UTC), when available.</summary>
    public DateTime? CreationTimeUtc { get; init; }

    /// <summary>Last modification time recorded in the image (UTC), when available.</summary>
    public DateTime? LastModificationTimeUtc { get; init; }
}
