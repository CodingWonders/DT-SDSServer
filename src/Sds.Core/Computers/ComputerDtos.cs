using Sds.Data.Entities;

namespace Sds.Core.Computers;

/// <summary>Body of POST/PUT /api/computers (WDS prestaged device properties).</summary>
public sealed record ComputerRequest
{
    /// <summary>Computer name (1 to 15 characters).</summary>
    public required string Name { get; init; }

    /// <summary>MAC address (any common notation).</summary>
    public string? MacAddress { get; init; }

    /// <summary>SMBIOS UUID.</summary>
    public string? SmbiosGuid { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Architecture (x86, amd64, arm64).</summary>
    public string? Architecture { get; init; }

    /// <summary>Join a domain after setup.</summary>
    public bool JoinDomain { get; init; } = true;

    /// <summary>Domain DNS name.</summary>
    public string? Domain { get; init; }

    /// <summary>Target OU distinguished name.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>User or group allowed to deploy the device.</summary>
    public string? User { get; init; }

    /// <summary>Join rights of <see cref="User"/>.</summary>
    public JoinRights JoinRights { get; init; } = JoinRights.JoinOnly;

    /// <summary>Referral server.</summary>
    public string? ReferralServer { get; init; }

    /// <summary>Boot program.</summary>
    public string? BootProgram { get; init; }

    /// <summary>Boot image path.</summary>
    public string? BootImagePath { get; init; }

    /// <summary>Device specific WDS client unattend XML.</summary>
    public string? ClientUnattend { get; init; }

    /// <summary>Image group the device is restricted to (by name).</summary>
    public string? ImageGroup { get; init; }

    /// <summary>Preselected install image.</summary>
    public int? InstallImageId { get; init; }
}

/// <summary>Known computer as returned by the API.</summary>
public sealed record ComputerDto
{
    /// <summary>Id.</summary>
    public int Id { get; init; }

    /// <summary>Name.</summary>
    public required string Name { get; init; }

    /// <summary>MAC address (AABBCCDDEEFF).</summary>
    public string? MacAddress { get; init; }

    /// <summary>SMBIOS UUID.</summary>
    public Guid? SmbiosGuid { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Architecture.</summary>
    public string? Architecture { get; init; }

    /// <summary>Join domain flag.</summary>
    public bool JoinDomain { get; init; }

    /// <summary>Domain.</summary>
    public string? Domain { get; init; }

    /// <summary>OU.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>User with join rights.</summary>
    public string? User { get; init; }

    /// <summary>Join rights.</summary>
    public JoinRights JoinRights { get; init; }

    /// <summary>Referral server.</summary>
    public string? ReferralServer { get; init; }

    /// <summary>Boot program.</summary>
    public string? BootProgram { get; init; }

    /// <summary>Boot image path.</summary>
    public string? BootImagePath { get; init; }

    /// <summary>True when a device specific client unattend is set.</summary>
    public bool HasClientUnattend { get; init; }

    /// <summary>Restricted image group name.</summary>
    public string? ImageGroup { get; init; }

    /// <summary>Preselected install image.</summary>
    public int? InstallImageId { get; init; }

    /// <summary>Origin.</summary>
    public ComputerOrigin Origin { get; init; }

    /// <summary>AD sync state.</summary>
    public DirectorySyncState DirectorySync { get; init; }

    /// <summary>AD distinguished name.</summary>
    public string? DistinguishedName { get; init; }

    /// <summary>Last AD error.</summary>
    public string? DirectoryError { get; init; }

    /// <summary>Last client contact.</summary>
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>Creation time.</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Last change time.</summary>
    public DateTime ModifiedUtc { get; init; }
}

/// <summary>Pending device as returned by the API.</summary>
public sealed record PendingDeviceDto(
    long RequestId,
    string? MacAddress,
    Guid? SmbiosGuid,
    string? Architecture,
    string? IpAddress,
    string? ReportedName,
    PendingDeviceStatus Status,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    DateTime? DecidedUtc,
    string? DecidedBy,
    string? Message,
    int? ComputerId);

/// <summary>Body of POST /api/pending-devices/{id}/approve. Null fields use server defaults.</summary>
public sealed record ApproveRequest
{
    /// <summary>Computer name; null generates one with the naming policy.</summary>
    public string? Name { get; init; }

    /// <summary>User the name is generated for and who receives join rights.</summary>
    public string? User { get; init; }

    /// <summary>Join rights.</summary>
    public JoinRights? JoinRights { get; init; }

    /// <summary>Join a domain.</summary>
    public bool? JoinDomain { get; init; }

    /// <summary>Domain.</summary>
    public string? Domain { get; init; }

    /// <summary>OU.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>Referral server.</summary>
    public string? ReferralServer { get; init; }

    /// <summary>Boot program.</summary>
    public string? BootProgram { get; init; }

    /// <summary>Boot image path.</summary>
    public string? BootImagePath { get; init; }

    /// <summary>Restricted image group.</summary>
    public string? ImageGroup { get; init; }

    /// <summary>Message returned to the client.</summary>
    public string? Message { get; init; }
}

/// <summary>Body of POST /api/pending-devices/{id}/reject.</summary>
public sealed record RejectRequest(string? Message);

/// <summary>Body of POST /api/client/register sent by a booting deployment client.</summary>
public sealed record ClientRegisterRequest
{
    /// <summary>MAC address of the boot NIC.</summary>
    public string? MacAddress { get; init; }

    /// <summary>SMBIOS UUID.</summary>
    public string? SmbiosGuid { get; init; }

    /// <summary>Client architecture.</summary>
    public string? Architecture { get; init; }

    /// <summary>Name currently reported by the client, if any.</summary>
    public string? ReportedName { get; init; }
}

/// <summary>Decision returned to a client.</summary>
public enum ClientDecisionStatus
{
    /// <summary>The client is a known computer; configuration is included.</summary>
    Known,

    /// <summary>Unknown client allowed by policy (respond to all, no approval).</summary>
    Allowed,

    /// <summary>Waiting for approval; poll again after the interval.</summary>
    Pending,

    /// <summary>An administrator rejected the client.</summary>
    Rejected,

    /// <summary>The server does not answer this client (policy).</summary>
    Ignored,
}

/// <summary>Configuration handed to an allowed or known client.</summary>
public sealed record ClientConfigurationDto
{
    /// <summary>Computer name, null when the client must generate one (POST /api/client/name).</summary>
    public string? Name { get; init; }

    /// <summary>Join domain flag.</summary>
    public bool JoinDomain { get; init; }

    /// <summary>Domain.</summary>
    public string? Domain { get; init; }

    /// <summary>OU.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>User with join rights.</summary>
    public string? User { get; init; }

    /// <summary>Join rights.</summary>
    public JoinRights JoinRights { get; init; }

    /// <summary>Referral server.</summary>
    public string? ReferralServer { get; init; }

    /// <summary>Boot program.</summary>
    public string? BootProgram { get; init; }

    /// <summary>Boot image path.</summary>
    public string? BootImagePath { get; init; }

    /// <summary>WDS client unattend XML (device specific, else server default for the architecture).</summary>
    public string? ClientUnattend { get; init; }

    /// <summary>Restricted image group.</summary>
    public string? ImageGroup { get; init; }

    /// <summary>Preselected install image.</summary>
    public int? InstallImageId { get; init; }
}

/// <summary>Response of the client registration and polling endpoints.</summary>
public sealed record ClientDecision
{
    /// <summary>Decision.</summary>
    public ClientDecisionStatus Status { get; init; }

    /// <summary>Pending request id (poll GET /api/client/requests/{id}).</summary>
    public long? RequestId { get; init; }

    /// <summary>Seconds to wait before polling again.</summary>
    public int? PollIntervalSeconds { get; init; }

    /// <summary>Message to display.</summary>
    public string? Message { get; init; }

    /// <summary>Configuration for Known and Allowed clients.</summary>
    public ClientConfigurationDto? Configuration { get; init; }
}

/// <summary>Body of POST /api/client/name.</summary>
public sealed record GenerateNameRequest(string? Username, string? First, string? Last, string? MacAddress);
