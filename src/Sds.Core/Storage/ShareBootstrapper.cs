using System.DirectoryServices.AccountManagement;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sds.Core.Storage;

/// <summary>
/// Creates the on disk structure, the SMB share and the local security groups, like the
/// WDS "Configure Server" wizard does for RemoteInstall.
/// </summary>
public sealed class ShareBootstrapper(ShareLayout layout, IOptions<SdsOptions> options, ILogger<ShareBootstrapper> logger)
{
    private readonly SdsOptions _options = options.Value;

    /// <summary>Runs every bootstrap step. Safe to call on every start (idempotent).</summary>
    public void Run()
    {
        // Step 1: local groups first, so the ACL step can reference them.
        if (_options.CreateLocalGroups)
        {
            EnsureLocalGroup(_options.AdministratorsGroup, "Members can administer the SDS deployment server.");
            EnsureLocalGroup(_options.OperatorsGroup, "Members have read only access to the SDS deployment server.");
        }

        // Step 2: folders. Default ACLs only when the root is created, so that permissions an
        // administrator changed later are never overwritten.
        var rootIsNew = !Directory.Exists(layout.Root);
        Directory.CreateDirectory(layout.Root);
        Directory.CreateDirectory(layout.PxeCore);
        Directory.CreateDirectory(layout.Images);
        Directory.CreateDirectory(layout.Drivers);
        Directory.CreateDirectory(layout.DataDirectory);
        Directory.CreateDirectory(layout.Staging);
        Directory.CreateDirectory(layout.WimTemp);
        Directory.CreateDirectory(layout.Logs);

        if (rootIsNew && _options.ApplyDefaultAcls)
        {
            ApplyRootAcl();
            ApplyPrivateAcl();
        }

        // Step 3: SMB shares. Only IMAGES and DRIVERS are shared (\\host\images and
        // \\host\drivers); the root and PXECORE are never shared.
        if (_options.CreateShares)
        {
            EnsureShare(layout.ImagesShareName, layout.Images, "SDS install images");
            EnsureShare(layout.DriversShareName, layout.Drivers, "SDS driver packages");
        }

        logger.LogInformation("Deployment share ready at {Root} (data in {Data})", layout.Root, layout.DataDirectory);
    }

    /// <summary>
    /// Root ACL: Administrators, SYSTEM and SDS Administrators full control, Authenticated
    /// Users read (clients read images and drivers over SMB, exactly like REMINST).
    /// </summary>
    private void ApplyRootAcl()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        var adminSid = TryResolve(_options.AdministratorsGroup);
        if (adminSid is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(layout.Root).SetAccessControl(security);
        logger.LogInformation("Default permissions applied to {Root}", layout.Root);
    }

    /// <summary>The data directory holds the database: only Administrators and SYSTEM.</summary>
    private void ApplyPrivateAcl()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        try
        {
            new DirectoryInfo(layout.DataDirectory).SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not restrict permissions on {Data}", layout.DataDirectory);
        }
    }

    /// <summary>
    /// Creates the SMB share <paramref name="name"/> for <paramref name="folder"/> through WMI
    /// (Win32_Share.Create) when it is missing. An existing share is never modified.
    /// </summary>
    private void EnsureShare(string name, string folder, string description)
    {
        try
        {
            using var existing = new ManagementObjectSearcher($"SELECT Name, Path FROM Win32_Share WHERE Name = '{name.Replace("'", "''")}'");
            foreach (ManagementObject share in existing.Get())
            {
                var path = (string)share["Path"];
                if (!string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Share {Share} already exists but points to {Path} instead of {Folder}", name, path, folder);
                }

                return;
            }

            // Share permissions copy what WDS sets on REMINST: SYSTEM and Administrators full,
            // Authenticated Users read, plus SDS Administrators full. Without an explicit
            // descriptor Windows grants only Everyone read, which blocks administrators from
            // writing over SMB (for example to copy driver files remotely). NTFS stays the
            // finer grained layer (image user filters).
            using var shareClass = new ManagementClass("Win32_Share");
            using var input = shareClass.GetMethodParameters("Create");
            input["Path"] = folder;
            input["Name"] = name;
            input["Type"] = 0u; // Disk drive.
            input["Description"] = description;
            input["Access"] = BuildShareDescriptor();
            using var output = shareClass.InvokeMethod("Create", input, null);
            var code = Convert.ToUInt32(output["ReturnValue"]);
            if (code == 0)
            {
                logger.LogInformation("Created SMB share \\\\{Server}\\{Share} for {Folder}", layout.ServerName, name, folder);
            }
            else
            {
                logger.LogWarning("Win32_Share.Create returned {Code} for {Share}", code, name);
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            logger.LogWarning(ex, "Could not create the SMB share {Share}", name);
        }
    }

    /// <summary>
    /// Builds the Win32_SecurityDescriptor passed to Win32_Share.Create. Each entry is a
    /// Win32_ACE whose trustee is identified by its binary SID.
    /// </summary>
    private ManagementBaseObject BuildShareDescriptor()
    {
        // Share level access masks, as written by the "Advanced Sharing" dialog.
        const uint FullAccess = 0x001F01FF;
        const uint ReadAccess = 0x001200A9;

        var entries = new List<(SecurityIdentifier Sid, uint Mask)>
        {
            (new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FullAccess),
            (new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FullAccess),
            (new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), ReadAccess),
        };

        var adminSid = TryResolve(_options.AdministratorsGroup);
        if (adminSid is not null)
        {
            entries.Add((adminSid, FullAccess));
        }

        var aces = new List<ManagementObject>();
        foreach (var (sid, mask) in entries)
        {
            var sidBytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidBytes, 0);

            var trustee = new ManagementClass("Win32_Trustee").CreateInstance();
            trustee["SID"] = sidBytes;

            var ace = new ManagementClass("Win32_ACE").CreateInstance();
            ace["Trustee"] = trustee;
            ace["AccessMask"] = mask;
            ace["AceFlags"] = 0u;
            ace["AceType"] = 0u; // Access allowed.
            aces.Add(ace);
        }

        var descriptor = new ManagementClass("Win32_SecurityDescriptor").CreateInstance();
        descriptor["DACL"] = aces.ToArray();
        descriptor["ControlFlags"] = 0x4u; // SE_DACL_PRESENT.
        return descriptor;
    }

    private void EnsureLocalGroup(string name, string description)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, name);
            if (group is not null)
            {
                return;
            }

            using var created = new GroupPrincipal(context, name) { Description = description };
            created.Save();
            logger.LogInformation("Created local group {Group}", name);
        }
        catch (Exception ex) when (ex is PrincipalException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Domain controllers have no local SAM groups; administrators can use
            // ExtraAdministratorGroups with domain groups instead.
            logger.LogWarning(ex, "Could not create local group {Group}", name);
        }
    }

    /// <summary>Resolves a local or domain account name to a SID, or null when unknown.</summary>
    public static SecurityIdentifier? TryResolve(string account)
    {
        // Try MACHINE\name first (local groups), then the name as given (domain lookup).
        var qualified = account.Contains('\\') ? account : Environment.MachineName + "\\" + account;
        return TryTranslate(qualified) ?? (qualified == account ? null : TryTranslate(account));
    }

    private static SecurityIdentifier? TryTranslate(string account)
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            // IdentityNotMappedException for unknown names; Win32Exception (for example 1788,
            // broken domain trust) when the domain cannot be queried. Both mean "unresolved".
            return null;
        }
    }
}
