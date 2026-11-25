using Sds.Data.Entities;

namespace Sds.Core.Drivers;

/// <summary>Driver package as returned by the API.</summary>
public sealed record DriverPackageDto
{
    /// <summary>Package id (GUID part of the folder name).</summary>
    public Guid Id { get; init; }

    /// <summary>Folder name: &lt;drvname&gt;_&lt;arch&gt;_{GUID}.</summary>
    public required string FolderName { get; init; }

    /// <summary>INF file name.</summary>
    public required string InfName { get; init; }

    /// <summary>Friendly name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Setup class.</summary>
    public string? ClassName { get; init; }

    /// <summary>Setup class GUID.</summary>
    public Guid? ClassGuid { get; init; }

    /// <summary>Provider.</summary>
    public string? Provider { get; init; }

    /// <summary>Version.</summary>
    public string? Version { get; init; }

    /// <summary>Driver date.</summary>
    public DateOnly? DriverDate { get; init; }

    /// <summary>Architecture.</summary>
    public required string Architecture { get; init; }

    /// <summary>Catalog file name.</summary>
    public string? CatalogFile { get; init; }

    /// <summary>True when a catalog file is present (signed package).</summary>
    public bool Signed { get; init; }

    /// <summary>Boot critical (storage or network) package.</summary>
    public bool BootCritical { get; init; }

    /// <summary>Enabled flag.</summary>
    public bool Enabled { get; init; }

    /// <summary>Package size.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Import time.</summary>
    public DateTime ImportedUtc { get; init; }

    /// <summary>Number of hardware ids.</summary>
    public int HardwareIdCount { get; init; }

    /// <summary>Driver groups the package belongs to.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>UNC path of the package folder.</summary>
    public required string UncPath { get; init; }

    /// <summary>Hardware ids (only filled by the detail endpoint).</summary>
    public IReadOnlyList<HardwareIdDto>? HardwareIds { get; init; }
}

/// <summary>Hardware id entry.</summary>
public sealed record HardwareIdDto(string HardwareId, string? Description);

/// <summary>Body of POST /api/drivers/import.</summary>
public sealed record DriverImportRequest
{
    /// <summary>Folder, INF file or ZIP file on the server. Ignored for uploads.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Only import these architectures; null imports every architecture the INF supports.</summary>
    public List<string>? Architectures { get; init; }

    /// <summary>Driver groups the packages are added to (must exist).</summary>
    public List<string>? DriverGroups { get; init; }

    /// <summary>Initial enabled state.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>An INF that was not imported.</summary>
public sealed record SkippedInf(string Inf, string Reason);

/// <summary>Result of an import job.</summary>
public sealed record DriverImportResult(List<DriverPackageDto> Imported, List<DriverPackageDto> Duplicates, List<SkippedInf> Skipped);

/// <summary>Body of PATCH /api/drivers/{id}.</summary>
public sealed record UpdateDriverRequest(string? DisplayName, bool? Enabled);

/// <summary>Driver group filter as exchanged with the API.</summary>
public sealed record DriverFilterDto(DriverFilterType Type, DriverFilterPolicy Policy, List<string> Values);

/// <summary>Driver group as returned by the API.</summary>
public sealed record DriverGroupDto(
    int Id,
    string Name,
    string? Description,
    bool Enabled,
    DriverGroupApplicability Applicability,
    IReadOnlyList<DriverFilterDto> Filters,
    int PackageCount,
    DateTime CreatedUtc);

/// <summary>Body of POST/PUT /api/driver-groups.</summary>
public sealed record DriverGroupRequest
{
    /// <summary>Group name.</summary>
    public required string Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Enabled flag.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Applicability.</summary>
    public DriverGroupApplicability Applicability { get; init; } = DriverGroupApplicability.Matching;

    /// <summary>Filters (replaces the existing list).</summary>
    public List<DriverFilterDto> Filters { get; init; } = [];
}

/// <summary>Body of POST /api/driver-groups/{name}/packages (add or remove).</summary>
public sealed record DriverGroupMembershipRequest(List<Guid> PackageIds);

/// <summary>
/// Body of POST /api/client/drivers/query: the client describes its hardware and the image it
/// installs; the server answers with the packages to inject (WDS driver group evaluation).
/// </summary>
public sealed record DriverQueryRequest
{
    /// <summary>Architecture of the image being installed.</summary>
    public required string Architecture { get; init; }

    /// <summary>SMBIOS manufacturer.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>SMBIOS model.</summary>
    public string? Model { get; init; }

    /// <summary>Chassis type.</summary>
    public string? ChassisType { get; init; }

    /// <summary>BIOS vendor.</summary>
    public string? BiosVendor { get; init; }

    /// <summary>BIOS version.</summary>
    public string? BiosVersion { get; init; }

    /// <summary>Install image being deployed; fills OS version, edition and language when not given.</summary>
    public int? InstallImageId { get; init; }

    /// <summary>OS version (major.minor.build...).</summary>
    public string? OsVersion { get; init; }

    /// <summary>OS edition.</summary>
    public string? OsEdition { get; init; }

    /// <summary>OS language.</summary>
    public string? OsLanguage { get; init; }

    /// <summary>Hardware and compatible ids of every device of the client.</summary>
    public List<string> HardwareIds { get; init; } = [];
}

/// <summary>One package selected for a client.</summary>
public sealed record DriverMatchDto(
    Guid PackageId,
    string DisplayName,
    string InfName,
    string Architecture,
    string? ClassName,
    bool BootCritical,
    string UncPath,
    string InfUnc,
    IReadOnlyList<string> MatchedHardwareIds,
    IReadOnlyList<string> Groups);
