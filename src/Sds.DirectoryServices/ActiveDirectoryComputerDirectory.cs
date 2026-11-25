using System.DirectoryServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sds.DirectoryServices;

/// <summary>Settings of the optional Active Directory provider (configuration section Sds:ActiveDirectory).</summary>
public sealed class ActiveDirectoryOptions
{
    /// <summary>Enables the provider. When false, <see cref="LocalComputerDirectory"/> is used.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// LDAP path of the domain or a domain controller, for example LDAP://dc01.contoso.com/DC=contoso,DC=com.
    /// Empty uses the domain of the server (serverless binding).
    /// </summary>
    public string? LdapPath { get; set; }

    /// <summary>OU used when a computer has none, for example OU=Deploy,DC=contoso,DC=com. Empty uses CN=Computers.</summary>
    public string? DefaultOrganizationalUnit { get; set; }

    /// <summary>Optional user for the LDAP bind; empty uses the service account.</summary>
    public string? Username { get; set; }

    /// <summary>Password for <see cref="Username"/>.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Mirrors known computers into Active Directory the way WDS prestages devices: a computer
/// object with sAMAccountName NAME$, the netbootGUID attribute holding the SMBIOS GUID (or the
/// MAC address padded to a GUID), netbootMachineFilePath holding the referral server and
/// optional join rights for a user.
/// </summary>
public sealed class ActiveDirectoryComputerDirectory(IOptions<ActiveDirectoryOptions> options, ILogger<ActiveDirectoryComputerDirectory> logger) : IComputerDirectory
{
    // userAccountControl = WORKSTATION_TRUST_ACCOUNT (0x1000) | PASSWD_NOTREQD (0x20),
    // the value Active Directory Users and Computers uses for prestaged accounts.
    private const int PrestagedAccountControl = 0x1000 | 0x20;

    // Extended right GUIDs used for "join only" rights, as granted by WDS and ADUC.
    private static readonly Guid ResetPassword = new("00299570-246d-11d0-a768-00aa006e0529");
    private static readonly Guid ValidatedDnsHostName = new("72e39547-7b18-11d1-adef-00c04fd8d5cd");
    private static readonly Guid ValidatedSpn = new("f3a64788-5306-11d1-a9c5-0000f80367c1");
    private static readonly Guid AccountRestrictions = new("4c164200-20c0-11d0-a768-00aa006e0529");

    private readonly ActiveDirectoryOptions _options = options.Value;

    /// <inheritdoc />
    public bool IsExternal => true;

    /// <inheritdoc />
    public Task<DirectoryResult> UpsertAsync(DirectoryComputer computer, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                using var existing = FindEntryByName(computer.Name);
                DirectoryEntry entry;
                if (existing is null)
                {
                    // Create CN=<name> in the requested OU (or default container).
                    using var container = Bind(computer.OrganizationalUnit ?? _options.DefaultOrganizationalUnit ?? DefaultComputersContainer());
                    entry = container.Children.Add("CN=" + EscapeRdn(computer.Name), "computer");
                    entry.Properties["sAMAccountName"].Value = computer.Name.ToUpperInvariant() + "$";
                    entry.Properties["userAccountControl"].Value = PrestagedAccountControl;
                    entry.CommitChanges();
                }
                else
                {
                    entry = existing.GetDirectoryEntry();
                }

                using (entry)
                {
                    SetOrClear(entry, "description", computer.Description);
                    SetOrClear(entry, "netbootMachineFilePath", computer.ReferralServer);

                    // netbootGUID is the identifier WDS matches PXE clients with.
                    var netbootGuid = ToNetbootGuid(computer.MacAddress, computer.SmbiosGuid);
                    if (netbootGuid is null)
                    {
                        entry.Properties["netbootGUID"].Clear();
                    }
                    else
                    {
                        entry.Properties["netbootGUID"].Value = netbootGuid.Value.ToByteArray();
                    }

                    // WDS keeps per device boot settings in netbootMirrorDataFile.
                    var mirror = new List<string>();
                    if (!string.IsNullOrEmpty(computer.BootProgram))
                    {
                        mirror.Add("BootProgram=" + computer.BootProgram);
                    }

                    if (!string.IsNullOrEmpty(computer.BootImagePath))
                    {
                        mirror.Add("BootImagePath=" + computer.BootImagePath);
                    }

                    SetOrClear(entry, "netbootMirrorDataFile", mirror.Count == 0 ? null : string.Join(';', mirror));
                    entry.CommitChanges();

                    if (!string.IsNullOrWhiteSpace(computer.JoinUser))
                    {
                        GrantJoinRights(entry, computer.JoinUser, computer.FullJoinRights);
                    }

                    var dn = (string?)entry.Properties["distinguishedName"].Value;
                    logger.LogInformation("Computer {Name} synchronised to Active Directory ({Dn})", computer.Name, dn);
                    return new DirectoryResult(true, dn, null);
                }
            }
            catch (Exception ex) when (ex is DirectoryServicesCOMException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IdentityNotMappedException)
            {
                logger.LogWarning(ex, "Active Directory update failed for {Name}", computer.Name);
                return new DirectoryResult(false, null, ex.Message);
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryResult> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                using var result = FindEntryByName(name);
                if (result is null)
                {
                    return DirectoryResult.NotApplicable;
                }

                using var entry = result.GetDirectoryEntry();
                entry.DeleteTree();
                return new DirectoryResult(true, null, null);
            }
            catch (Exception ex) when (ex is DirectoryServicesCOMException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Active Directory delete failed for {Name}", name);
                return new DirectoryResult(false, null, ex.Message);
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var result = FindEntryByName(name);
            return result is not null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryComputer?> FindAsync(string? macAddress, Guid? smbiosGuid, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            // Search by every identifier we have; WDS tries the SMBIOS GUID first.
            foreach (var guid in new[] { smbiosGuid is null ? (Guid?)null : smbiosGuid, ToNetbootGuid(macAddress, null) })
            {
                if (guid is null)
                {
                    continue;
                }

                using var root = Bind(null);
                using var searcher = new DirectorySearcher(root, $"(&(objectClass=computer)(netbootGUID={OctetFilter(guid.Value.ToByteArray())}))",
                    ["name", "description", "netbootMachineFilePath", "distinguishedName"]);
                using var found = new DisposableResult(searcher.FindOne());
                if (found.Result is not null)
                {
                    var dn = (string)found.Result.Properties["distinguishedName"][0];
                    return new DirectoryComputer
                    {
                        Name = (string)found.Result.Properties["name"][0],
                        Description = found.Result.Properties["description"].Count > 0 ? (string)found.Result.Properties["description"][0] : null,
                        ReferralServer = found.Result.Properties["netbootMachineFilePath"].Count > 0 ? (string)found.Result.Properties["netbootMachineFilePath"][0] : null,
                        OrganizationalUnit = dn[(dn.IndexOf(',') + 1)..],
                        MacAddress = macAddress,
                        SmbiosGuid = smbiosGuid,
                    };
                }
            }

            return (DirectoryComputer?)null;
        }, cancellationToken);

    /// <summary>
    /// Builds the netbootGUID value. WDS stores the SMBIOS UUID when known; otherwise the MAC
    /// address padded with zeros: 00000000-0000-0000-0000-AABBCCDDEEFF.
    /// </summary>
    public static Guid? ToNetbootGuid(string? macAddress, Guid? smbiosGuid)
    {
        if (smbiosGuid is not null)
        {
            return smbiosGuid;
        }

        return string.IsNullOrEmpty(macAddress) ? null : Guid.Parse("00000000-0000-0000-0000-" + macAddress);
    }

    private DirectoryEntry Bind(string? distinguishedName)
    {
        // Build LDAP://server/DN or LDAP://DN depending on the configured path.
        string path;
        if (distinguishedName is null)
        {
            path = string.IsNullOrEmpty(_options.LdapPath) ? "LDAP://RootDSE" : _options.LdapPath;
            if (path.EndsWith("RootDSE", StringComparison.OrdinalIgnoreCase))
            {
                using var rootDse = new DirectoryEntry(path, _options.Username, _options.Password);
                path = "LDAP://" + (string)rootDse.Properties["defaultNamingContext"].Value!;
            }
        }
        else
        {
            var server = ServerPrefix();
            path = "LDAP://" + server + distinguishedName;
        }

        return new DirectoryEntry(path, _options.Username, _options.Password);
    }

    private string ServerPrefix()
    {
        // Extract "server/" from LDAP://server/DC=... when the admin pinned a domain controller.
        if (string.IsNullOrEmpty(_options.LdapPath))
        {
            return string.Empty;
        }

        var rest = _options.LdapPath["LDAP://".Length..];
        var slash = rest.IndexOf('/');
        return slash > 0 ? rest[..(slash + 1)] : string.Empty;
    }

    private string DefaultComputersContainer()
    {
        using var root = Bind(null);
        return "CN=Computers," + (string)root.Properties["distinguishedName"].Value!;
    }

    private DisposableResult? FindEntryByName(string name)
    {
        using var root = Bind(null);
        using var searcher = new DirectorySearcher(root, $"(&(objectClass=computer)(sAMAccountName={EscapeFilter(name.ToUpperInvariant())}$))");
        var result = searcher.FindOne();
        return result is null ? null : new DisposableResult(result);
    }

    private static void GrantJoinRights(DirectoryEntry entry, string account, bool full)
    {
        var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        var security = entry.ObjectSecurity;
        if (full)
        {
            security.AddAccessRule(new ActiveDirectoryAccessRule(sid, ActiveDirectoryRights.GenericAll, AccessControlType.Allow));
        }
        else
        {
            // "Join only": the rights needed by the domain join of an existing account.
            security.AddAccessRule(new ActiveDirectoryAccessRule(sid, ActiveDirectoryRights.ExtendedRight, AccessControlType.Allow, ResetPassword));
            security.AddAccessRule(new ActiveDirectoryAccessRule(sid, ActiveDirectoryRights.Self, AccessControlType.Allow, ValidatedDnsHostName));
            security.AddAccessRule(new ActiveDirectoryAccessRule(sid, ActiveDirectoryRights.Self, AccessControlType.Allow, ValidatedSpn));
            security.AddAccessRule(new ActiveDirectoryAccessRule(sid, ActiveDirectoryRights.WriteProperty, AccessControlType.Allow, AccountRestrictions));
        }

        entry.CommitChanges();
    }

    private static void SetOrClear(DirectoryEntry entry, string attribute, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (entry.Properties.Contains(attribute))
            {
                entry.Properties[attribute].Clear();
            }
        }
        else
        {
            entry.Properties[attribute].Value = value;
        }
    }

    /// <summary>Encodes bytes as an LDAP octet string filter: \AA\BB...</summary>
    private static string OctetFilter(byte[] bytes) => string.Concat(bytes.Select(b => "\\" + b.ToString("x2")));

    /// <summary>Escapes LDAP filter special characters (RFC 4515).</summary>
    private static string EscapeFilter(string value) => value
        .Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    /// <summary>Escapes RDN special characters (RFC 4514).</summary>
    private static string EscapeRdn(string value) => value
        .Replace("\\", "\\\\").Replace(",", "\\,").Replace("+", "\\+").Replace("\"", "\\\"").Replace("<", "\\<").Replace(">", "\\>").Replace(";", "\\;");

    /// <summary>Wraps a SearchResult so it can be used in using statements.</summary>
    private sealed class DisposableResult(SearchResult? result) : IDisposable
    {
        public SearchResult? Result { get; } = result;

        public DirectoryEntry GetDirectoryEntry() => Result!.GetDirectoryEntry();

        public void Dispose()
        {
            // SearchResult holds no unmanaged state by itself; the wrapper only makes the
            // calling code uniform.
        }
    }
}
