namespace Sds.Data.Entities;

/// <summary>
/// A driver package: one INF and its files for one architecture, stored in
/// DRIVERS\&lt;drvname&gt;_&lt;arch&gt;_{GUID}.
/// </summary>
public class DriverPackage
{
    /// <summary>Package id; also the GUID part of the folder name.</summary>
    public Guid Id { get; set; }

    /// <summary>Folder name inside DRIVERS, for example e1d68x64_amd64_{...}.</summary>
    public required string FolderName { get; set; }

    /// <summary>INF file name relative to the package folder.</summary>
    public required string InfName { get; set; }

    /// <summary>Friendly name (defaults to the INF base name, editable like in WDS).</summary>
    public required string DisplayName { get; set; }

    /// <summary>Device setup class name (Class in [Version]).</summary>
    public string? ClassName { get; set; }

    /// <summary>Device setup class GUID (ClassGuid in [Version]).</summary>
    public Guid? ClassGuid { get; set; }

    /// <summary>Provider (Provider in [Version]).</summary>
    public string? Provider { get; set; }

    /// <summary>Driver version from DriverVer.</summary>
    public string? Version { get; set; }

    /// <summary>Driver date from DriverVer.</summary>
    public DateOnly? DriverDate { get; set; }

    /// <summary>Architecture: x86, amd64, arm64 or ia64.</summary>
    public required string Architecture { get; set; }

    /// <summary>Catalog file name, null for unsigned packages.</summary>
    public string? CatalogFile { get; set; }

    /// <summary>True when the package is a boot critical class (storage, network).</summary>
    public bool BootCritical { get; set; }

    /// <summary>Disabled packages are never returned to clients.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>SHA 256 of the INF file, used with the architecture to detect duplicates.</summary>
    public required string InfHash { get; set; }

    /// <summary>Total size in bytes of the package folder.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Original location the package was imported from.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Import time (UTC).</summary>
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Hardware ids handled by the package.</summary>
    public List<DriverHardwareId> HardwareIds { get; set; } = [];

    /// <summary>Groups the package belongs to.</summary>
    public List<DriverGroup> Groups { get; set; } = [];
}

/// <summary>A PnP hardware or compatible id supported by a driver package.</summary>
public class DriverHardwareId
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Owning package.</summary>
    public Guid DriverPackageId { get; set; }

    /// <summary>Hardware id in upper case, for example PCI\VEN_8086&amp;DEV_15B8.</summary>
    public required string HardwareId { get; set; }

    /// <summary>Model description from the INF models section.</summary>
    public string? Description { get; set; }
}

/// <summary>A driver group with optional filters (WDS driver groups).</summary>
public class DriverGroup
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Unique group name.</summary>
    public required string Name { get; set; }

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Disabled groups are ignored when clients query drivers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which packages of the group are installed.</summary>
    public DriverGroupApplicability Applicability { get; set; } = DriverGroupApplicability.Matching;

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Filters; all of them must be satisfied for the group to apply.</summary>
    public List<DriverGroupFilter> Filters { get; set; } = [];

    /// <summary>Packages of the group.</summary>
    public List<DriverPackage> Packages { get; set; } = [];
}

/// <summary>One filter of a driver group.</summary>
public class DriverGroupFilter
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Owning group.</summary>
    public int DriverGroupId { get; set; }

    /// <summary>What the filter looks at.</summary>
    public DriverFilterType Type { get; set; }

    /// <summary>Include or exclude.</summary>
    public DriverFilterPolicy Policy { get; set; }

    /// <summary>Values to compare with (case insensitive).</summary>
    public List<string> Values { get; set; } = [];
}
