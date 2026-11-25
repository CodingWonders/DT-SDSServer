using Sds.Data.Entities;

namespace Sds.Core.Images;

/// <summary>Image group as returned by the API.</summary>
public sealed record ImageGroupDto(
    int Id,
    string Name,
    string? Description,
    int ImageCount,
    long ResourceFileBytes,
    string UncPath,
    DateTime CreatedUtc,
    DateTime ModifiedUtc);

/// <summary>Body of POST /api/image-groups.</summary>
public sealed record CreateImageGroupRequest(string Name, string? Description);

/// <summary>Body of PATCH /api/image-groups/{name}. Null fields are left unchanged.</summary>
public sealed record UpdateImageGroupRequest(string? Name, string? Description);

/// <summary>Install image as returned by the API.</summary>
public sealed record ImageDto
{
    /// <summary>Image id.</summary>
    public int Id { get; init; }

    /// <summary>Group id.</summary>
    public int GroupId { get; init; }

    /// <summary>Group name.</summary>
    public required string GroupName { get; init; }

    /// <summary>Metadata WIM file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Image name.</summary>
    public required string Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Architecture.</summary>
    public required string Architecture { get; init; }

    /// <summary>Product name.</summary>
    public string? ProductName { get; init; }

    /// <summary>Edition id.</summary>
    public string? EditionId { get; init; }

    /// <summary>Installation type.</summary>
    public string? InstallationType { get; init; }

    /// <summary>Product type.</summary>
    public string? ProductType { get; init; }

    /// <summary>FLAGS value.</summary>
    public string? Flags { get; init; }

    /// <summary>OS version.</summary>
    public string? Version { get; init; }

    /// <summary>Service pack level.</summary>
    public int ServicePackLevel { get; init; }

    /// <summary>HAL.</summary>
    public string? Hal { get; init; }

    /// <summary>Languages.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Default language.</summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>Uncompressed size.</summary>
    public long TotalBytes { get; init; }

    /// <summary>File count.</summary>
    public long FileCount { get; init; }

    /// <summary>Priority (lower first).</summary>
    public int Priority { get; init; }

    /// <summary>Enabled flag.</summary>
    public bool Enabled { get; init; }

    /// <summary>True when an image unattend file is configured.</summary>
    public bool HasUnattend { get; init; }

    /// <summary>Status.</summary>
    public ImageStatus Status { get; init; }

    /// <summary>Status message.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>UNC path of the metadata WIM.</summary>
    public required string MetadataUnc { get; init; }

    /// <summary>UNC path of Res.RWM.</summary>
    public required string ResourceUnc { get; init; }

    /// <summary>UNC path of the image unattend file, when set.</summary>
    public string? UnattendUnc { get; init; }

    /// <summary>Creation time.</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Last change time.</summary>
    public DateTime ModifiedUtc { get; init; }
}

/// <summary>
/// Body of POST /api/image-groups/{group}/images: adds one or several images of a WIM to the
/// group (WDS "Add Install Image").
/// </summary>
public sealed record AddImageRequest
{
    /// <summary>Path of the source WIM or ESD on the server (local or UNC). Ignored for uploads.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Indexes to add; null or empty adds every image of the source.</summary>
    public List<int>? Indexes { get; init; }

    /// <summary>Metadata WIM file name; defaults to the source file name. WDS style "-(n)" suffixes are added on collision.</summary>
    public string? FileName { get; init; }

    /// <summary>New image name (only allowed when adding a single image).</summary>
    public string? Name { get; init; }

    /// <summary>New description (only allowed when adding a single image).</summary>
    public string? Description { get; init; }

    /// <summary>Initial priority.</summary>
    public int? Priority { get; init; }

    /// <summary>Initial enabled state.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Optional image unattend XML applied to every added image.</summary>
    public string? UnattendXml { get; init; }
}

/// <summary>Body of PATCH /api/images/{id}. Null fields are left unchanged.</summary>
public sealed record UpdateImageRequest(string? Name, string? Description, bool? Enabled, int? Priority);

/// <summary>Body of POST /api/images/{id}/export.</summary>
public sealed record ExportImageRequest
{
    /// <summary>
    /// Destination WIM path on the server. Null exports to the staging area; the file can then
    /// be downloaded from GET /api/jobs/{id}/file.
    /// </summary>
    public string? DestinationPath { get; init; }

    /// <summary>Append to an existing destination WIM instead of failing.</summary>
    public bool Append { get; init; }

    /// <summary>Name for the exported image.</summary>
    public string? Name { get; init; }

    /// <summary>Description for the exported image.</summary>
    public string? Description { get; init; }
}

/// <summary>Body of POST /api/images/{id}/copy (and move).</summary>
public sealed record CopyImageRequest(string TargetGroup, string? FileName, string? Name, string? Description);

/// <summary>Body of POST /api/images/{id}/replace (WDS "Replace Image").</summary>
public sealed record ReplaceImageRequest(string? SourcePath, int Index = 1);

/// <summary>Result of an export job.</summary>
public sealed record ExportResult(string Path, long Bytes, int Index, string Name, bool Staged);

/// <summary>Result of a compaction job.</summary>
public sealed record CompactResult(string Group, long BytesReclaimed, long ResourceFileBytes);

/// <summary>
/// Image entry visible to a deployment client. FileName, Priority and ModifiedUtc mirror the
/// fields of Get-WdsInstallImage that WDS based client scripts select and display.
/// </summary>
public sealed record ClientImageDto(
    int Id,
    string Group,
    string Name,
    string? Description,
    string Architecture,
    IReadOnlyList<string> Languages,
    string? DefaultLanguage,
    string? Version,
    long TotalBytes,
    string MetadataUnc,
    string ResourceUnc,
    string? UnattendUnc,
    string FileName,
    int Priority,
    DateTime ModifiedUtc);
