namespace Sds.Data.Entities;

/// <summary>
/// A known computer (WDS prestaged device). Identified by MAC address or SMBIOS GUID,
/// exactly like the netbootGUID attribute WDS uses in Active Directory.
/// </summary>
public class Computer
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Computer (NetBIOS) name. Unique.</summary>
    public required string Name { get; set; }

    /// <summary>MAC address in canonical form AABBCCDDEEFF, or null.</summary>
    public string? MacAddress { get; set; }

    /// <summary>SMBIOS UUID of the machine, or null.</summary>
    public Guid? SmbiosGuid { get; set; }

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Client architecture reported at boot (x86, amd64, arm64), if known.</summary>
    public string? Architecture { get; set; }

    /// <summary>Whether WDS client should join the machine to a domain after setup.</summary>
    public bool JoinDomain { get; set; } = true;

    /// <summary>DNS name of the domain to join.</summary>
    public string? Domain { get; set; }

    /// <summary>Distinguished name of the OU where the computer account lives.</summary>
    public string? OrganizationalUnit { get; set; }

    /// <summary>User or group allowed to deploy and join this computer.</summary>
    public string? User { get; set; }

    /// <summary>Rights granted to <see cref="User"/> on the computer account.</summary>
    public JoinRights JoinRights { get; set; } = JoinRights.JoinOnly;

    /// <summary>Server the client should be referred to (WDS referral server).</summary>
    public string? ReferralServer { get; set; }

    /// <summary>Boot program path for this client (WDS per device boot program).</summary>
    public string? BootProgram { get; set; }

    /// <summary>Boot image path for this client (WDS per device boot image).</summary>
    public string? BootImagePath { get; set; }

    /// <summary>WDS client unattend file content for this device, or null to use the server default.</summary>
    public string? ClientUnattend { get; set; }

    /// <summary>Image group the client is restricted to, if any.</summary>
    public int? ImageGroupId { get; set; }

    /// <summary>Install image preselected for the client, if any.</summary>
    public int? InstallImageId { get; set; }

    /// <summary>How the computer was created.</summary>
    public ComputerOrigin Origin { get; set; } = ComputerOrigin.Prestaged;

    /// <summary>AD synchronisation state.</summary>
    public DirectorySyncState DirectorySync { get; set; } = DirectorySyncState.NotApplicable;

    /// <summary>Distinguished name of the AD computer object, when synchronised.</summary>
    public string? DistinguishedName { get; set; }

    /// <summary>Last AD error, when <see cref="DirectorySync"/> is Failed.</summary>
    public string? DirectoryError { get; set; }

    /// <summary>Last time the client contacted SDS (UTC).</summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last change time (UTC).</summary>
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A client that contacted SDS while the response policy requires approval
/// (WDS "Pending Devices"). The id is the request id given to the client.
/// </summary>
public class PendingDevice
{
    /// <summary>Request id.</summary>
    public long Id { get; set; }

    /// <summary>MAC address, canonical form.</summary>
    public string? MacAddress { get; set; }

    /// <summary>SMBIOS UUID.</summary>
    public Guid? SmbiosGuid { get; set; }

    /// <summary>Client architecture.</summary>
    public string? Architecture { get; set; }

    /// <summary>IP address the request came from.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Host name reported by the client, if any.</summary>
    public string? ReportedName { get; set; }

    /// <summary>Approval state.</summary>
    public PendingDeviceStatus Status { get; set; } = PendingDeviceStatus.Pending;

    /// <summary>First contact (UTC).</summary>
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last poll (UTC).</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Time of approval or rejection (UTC).</summary>
    public DateTime? DecidedUtc { get; set; }

    /// <summary>Administrator who approved or rejected the device.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Message shown to the client (WDS approval message).</summary>
    public string? Message { get; set; }

    /// <summary>Known computer created at approval.</summary>
    public int? ComputerId { get; set; }
}

/// <summary>Server wide WDS client unattend file for one architecture.</summary>
public class ClientUnattend
{
    /// <summary>Architecture key (x86, amd64, arm64, ia64).</summary>
    public required string Architecture { get; set; }

    /// <summary>Unattend XML content.</summary>
    public required string Content { get; set; }

    /// <summary>Last change time (UTC).</summary>
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
