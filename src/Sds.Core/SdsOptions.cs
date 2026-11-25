using Sds.Imaging;

namespace Sds.Core;

/// <summary>
/// Infrastructure configuration (appsettings.json section "Sds"). Policy settings that an
/// administrator changes at run time (response policy, naming policy...) live in the database
/// instead, see <see cref="Settings.ServerSettings"/>.
/// </summary>
public sealed class SdsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string Section = "Sds";

    /// <summary>
    /// Volume holding the deployment folders, for example "E:". The root is always
    /// &lt;Volume&gt;\SDSREMINST, mirroring the WDS RemoteInstall folder. The root itself is
    /// not shared: only IMAGES and DRIVERS are (see <see cref="ImagesShareName"/>).
    /// </summary>
    public string Volume { get; set; } = "C:";

    /// <summary>
    /// Optional full override of the root path. Only meant for tests and development; in
    /// production leave it empty so the root is &lt;Volume&gt;\SDSREMINST.
    /// </summary>
    public string? RootOverride { get; set; }

    /// <summary>
    /// Name of the SMB share of the IMAGES folder (\\host\images). The root folder and
    /// PXECORE are never shared; clients only reach IMAGES and DRIVERS.
    /// </summary>
    public string ImagesShareName { get; set; } = "images";

    /// <summary>Name of the SMB share of the DRIVERS folder (\\host\drivers).</summary>
    public string DriversShareName { get; set; } = "drivers";

    /// <summary>Create the IMAGES and DRIVERS shares at startup when they do not exist.</summary>
    public bool CreateShares { get; set; } = true;

    /// <summary>Apply the default NTFS permissions when the root folder is created.</summary>
    public bool ApplyDefaultAcls { get; set; } = true;

    /// <summary>Create the local security groups at startup when they do not exist.</summary>
    public bool CreateLocalGroups { get; set; } = true;

    /// <summary>
    /// Folder for the database, logs and staging files. Kept outside SDSREMINST so the
    /// root only contains PXECORE, IMAGES and DRIVERS. Defaults to %ProgramData%\SDS.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>Compression used for new Res.RWM and metadata WIM files.</summary>
    public WimCompression Compression { get; set; } = WimCompression.Lzx;

    /// <summary>Maximum number of background jobs running at the same time.</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>Hours before staged exports and uploads are deleted.</summary>
    public int StagingRetentionHours { get; set; } = 24;

    /// <summary>Local group whose members administer SDS (created at startup).</summary>
    public string AdministratorsGroup { get; set; } = "SDS Administrators";

    /// <summary>Local group whose members have read only access to SDS (created at startup).</summary>
    public string OperatorsGroup { get; set; } = "SDS Operators";

    /// <summary>Additional (for example domain) groups granted administrator rights.</summary>
    public List<string> ExtraAdministratorGroups { get; set; } = [];

    /// <summary>Additional (for example domain) groups granted read only rights.</summary>
    public List<string> ExtraOperatorGroups { get; set; } = [];

    /// <summary>
    /// Host name used to build UNC paths returned to clients. Empty uses the machine DNS name.
    /// </summary>
    public string? PublicServerName { get; set; }

    /// <summary>Resolved data directory.</summary>
    public string ResolvedDataDirectory =>
        string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SDS")
            : DataDirectory;
}
