using System.Security.AccessControl;
using System.Security.Principal;

namespace Sds.Core.Security;

/// <summary>Simplified access levels exposed through the API (same choices as the WDS UI).</summary>
public enum AclAccess
{
    /// <summary>Read and execute (list and download).</summary>
    Read,

    /// <summary>Modify.</summary>
    Modify,

    /// <summary>Full control.</summary>
    FullControl,
}

/// <summary>One access control entry as seen by API clients.</summary>
public sealed record AclEntryDto
{
    /// <summary>Account name (DOMAIN\name) or SID string.</summary>
    public required string Identity { get; init; }

    /// <summary>Access level.</summary>
    public AclAccess Access { get; init; } = AclAccess.Read;

    /// <summary>True for allow, false for deny.</summary>
    public bool Allow { get; init; } = true;

    /// <summary>True when the entry is inherited (read only).</summary>
    public bool Inherited { get; init; }

    /// <summary>Raw FileSystemRights value (informational).</summary>
    public string? Rights { get; init; }
}

/// <summary>An ACL as seen by API clients.</summary>
public sealed record AclDto
{
    /// <summary>When true, inherited entries from the parent folder apply.</summary>
    public bool InheritFromParent { get; init; } = true;

    /// <summary>Entries.</summary>
    public List<AclEntryDto> Entries { get; init; } = [];
}

/// <summary>
/// Reads and writes NTFS permissions on group folders and image files. In WDS the NTFS ACL of
/// an install image file is the user filter that decides who can see the image in the
/// client image list; SDS applies the same rule (see <see cref="CanRead"/>).
/// </summary>
public static class FileAclService
{
    /// <summary>Reads the ACL of a file or folder.</summary>
    public static AclDto Get(string path)
    {
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

        var entries = new List<AclEntryDto>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            entries.Add(new AclEntryDto
            {
                Identity = Translate((SecurityIdentifier)rule.IdentityReference),
                Access = ToAccess(rule.FileSystemRights),
                Allow = rule.AccessControlType == AccessControlType.Allow,
                Inherited = rule.IsInherited,
                Rights = rule.FileSystemRights.ToString(),
            });
        }

        return new AclDto { InheritFromParent = !security.AreAccessRulesProtected, Entries = entries };
    }

    /// <summary>
    /// Replaces the explicit entries of a file or folder. Inherited entries in the input are
    /// ignored; inheritance is controlled by <see cref="AclDto.InheritFromParent"/>.
    /// </summary>
    public static void Set(string path, AclDto acl)
    {
        var isDirectory = Directory.Exists(path);
        FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();

        // When inheritance is turned off nothing is copied: the explicit list is the whole ACL.
        security.SetAccessRuleProtection(!acl.InheritFromParent, preserveInheritance: false);
        var inherit = isDirectory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;

        foreach (var entry in acl.Entries.Where(e => !e.Inherited))
        {
            var sid = ResolveIdentity(entry.Identity);
            var rights = entry.Access switch
            {
                AclAccess.FullControl => FileSystemRights.FullControl,
                AclAccess.Modify => FileSystemRights.Modify,
                _ => FileSystemRights.ReadAndExecute,
            };
            security.AddAccessRule(new FileSystemAccessRule(sid, rights, inherit, PropagationFlags.None, entry.Allow ? AccessControlType.Allow : AccessControlType.Deny));
        }

        if (isDirectory)
        {
            new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
        }
        else
        {
            new FileInfo(path).SetAccessControl((FileSecurity)security);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="identity"/> can read <paramref name="path"/>. The
    /// check impersonates the caller and opens the file, which lets Windows evaluate the
    /// full ACL (groups, deny entries, inheritance) exactly as SMB access would.
    /// </summary>
    public static bool CanRead(WindowsIdentity identity, string path)
    {
        try
        {
            return WindowsIdentity.RunImpersonated(identity.AccessToken, () =>
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return true;
            });
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static SecurityIdentifier ResolveIdentity(string identity)
    {
        if (identity.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            return new SecurityIdentifier(identity);
        }

        try
        {
            return (SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            // Unknown name, or the domain could not be reached (Win32Exception).
            throw new SdsValidationException($"Account '{identity}' could not be resolved: {ex.Message}");
        }
    }

    private static string Translate(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }

    private static AclAccess ToAccess(FileSystemRights rights)
    {
        if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
        {
            return AclAccess.FullControl;
        }

        return (rights & FileSystemRights.Modify) == FileSystemRights.Modify ? AclAccess.Modify : AclAccess.Read;
    }
}
