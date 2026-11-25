namespace Sds.DirectoryServices;

/// <summary>
/// Data exchanged with a computer directory. Mirrors the attributes WDS writes on computer
/// objects when prestaging a device in Active Directory.
/// </summary>
public sealed record DirectoryComputer
{
    /// <summary>Computer (NetBIOS) name, without the trailing dollar sign.</summary>
    public required string Name { get; init; }

    /// <summary>MAC address in canonical form AABBCCDDEEFF.</summary>
    public string? MacAddress { get; init; }

    /// <summary>SMBIOS UUID.</summary>
    public Guid? SmbiosGuid { get; init; }

    /// <summary>Distinguished name of the target OU; null uses the default computers container.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>Description written on the object.</summary>
    public string? Description { get; init; }

    /// <summary>Referral server (written to netbootMachineFilePath, like WDS).</summary>
    public string? ReferralServer { get; init; }

    /// <summary>Boot program path (written to netbootMirrorDataFile).</summary>
    public string? BootProgram { get; init; }

    /// <summary>Boot image path (written to netbootMirrorDataFile).</summary>
    public string? BootImagePath { get; init; }

    /// <summary>Account (DOMAIN\user or group) that receives join rights on the object.</summary>
    public string? JoinUser { get; init; }

    /// <summary>True for full control, false for join only rights.</summary>
    public bool FullJoinRights { get; init; }
}

/// <summary>Outcome of a directory write.</summary>
/// <param name="Success">True when the directory is in sync.</param>
/// <param name="DistinguishedName">Distinguished name of the object, when known.</param>
/// <param name="Error">Error text on failure.</param>
public sealed record DirectoryResult(bool Success, string? DistinguishedName, string? Error)
{
    /// <summary>Result used when no directory is configured.</summary>
    public static DirectoryResult NotApplicable { get; } = new(true, null, null);
}

/// <summary>
/// Abstraction over where known computers are mirrored. The SDS database is always the
/// source of truth; an implementation may additionally keep Active Directory in sync.
/// </summary>
public interface IComputerDirectory
{
    /// <summary>True when an external directory (Active Directory) is in use.</summary>
    bool IsExternal { get; }

    /// <summary>Creates or updates the computer object.</summary>
    Task<DirectoryResult> UpsertAsync(DirectoryComputer computer, CancellationToken cancellationToken = default);

    /// <summary>Deletes the computer object, if it exists.</summary>
    Task<DirectoryResult> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns true when a computer object with this name already exists.</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a computer object by MAC address or SMBIOS GUID (netbootGUID attribute).
    /// Returns null when not found or when no external directory is configured.
    /// </summary>
    Task<DirectoryComputer?> FindAsync(string? macAddress, Guid? smbiosGuid, CancellationToken cancellationToken = default);
}

/// <summary>Directory used when Active Directory integration is disabled: does nothing.</summary>
public sealed class LocalComputerDirectory : IComputerDirectory
{
    /// <inheritdoc />
    public bool IsExternal => false;

    /// <inheritdoc />
    public Task<DirectoryResult> UpsertAsync(DirectoryComputer computer, CancellationToken cancellationToken = default) =>
        Task.FromResult(DirectoryResult.NotApplicable);

    /// <inheritdoc />
    public Task<DirectoryResult> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(DirectoryResult.NotApplicable);

    /// <inheritdoc />
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task<DirectoryComputer?> FindAsync(string? macAddress, Guid? smbiosGuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<DirectoryComputer?>(null);
}
