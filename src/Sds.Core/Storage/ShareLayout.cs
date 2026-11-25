using System.Net;
using Microsoft.Extensions.Options;
using Sds.Imaging;

namespace Sds.Core.Storage;

/// <summary>
/// Single place that knows where everything lives on disk.
/// </summary>
/// <remarks>
/// <code>
/// &lt;Volume&gt;\SDSREMINST\              not shared
///   PXECORE\                         not shared; SharpPXE and the single boot image (not managed by SDS)
///   IMAGES\                          shared as \\host\images
///   IMAGES\&lt;Group&gt;\                  Res.RWM + one metadata WIM per image
///   IMAGES\&lt;Group&gt;\&lt;Image&gt;\Unattend\ImageUnattend.xml   per image unattend (WDS location)
///   DRIVERS\                         shared as \\host\drivers
///   DRIVERS\&lt;drvname&gt;_&lt;arch&gt;_{GUID}\   one folder per driver package
/// %ProgramData%\SDS\
///   sds.db, Staging\, Logs\          kept off the share on purpose
/// </code>
/// </remarks>
public sealed class ShareLayout
{
    /// <summary>Folder name of the PXE core (SharpPXE) area.</summary>
    public const string PxeCoreFolder = "PXECORE";

    /// <summary>Folder name of the install image groups.</summary>
    public const string ImagesFolder = "IMAGES";

    /// <summary>Folder name of the driver packages.</summary>
    public const string DriversFolder = "DRIVERS";

    /// <summary>Root folder name created at the root of the volume.</summary>
    public const string RootFolder = "SDSREMINST";

    private readonly SdsOptions _options;

    /// <summary>Creates the layout from configuration.</summary>
    public ShareLayout(IOptions<SdsOptions> options)
    {
        _options = options.Value;

        // Root is <Volume>\SDSREMINST unless a test overrides it.
        var volume = _options.Volume.TrimEnd('\\', '/');
        if (!volume.EndsWith(':'))
        {
            volume += ":";
        }

        Root = string.IsNullOrWhiteSpace(_options.RootOverride)
            ? Path.Combine(volume + "\\", RootFolder)
            : Path.GetFullPath(_options.RootOverride);

        DataDirectory = _options.ResolvedDataDirectory;
        ServerName = string.IsNullOrWhiteSpace(_options.PublicServerName) ? GetDnsName() : _options.PublicServerName!;
    }

    /// <summary>Full path of SDSREMINST.</summary>
    public string Root { get; }

    /// <summary>Full path of PXECORE.</summary>
    public string PxeCore => Path.Combine(Root, PxeCoreFolder);

    /// <summary>Full path of IMAGES.</summary>
    public string Images => Path.Combine(Root, ImagesFolder);

    /// <summary>Full path of DRIVERS.</summary>
    public string Drivers => Path.Combine(Root, DriversFolder);

    /// <summary>Folder with the database and other private files.</summary>
    public string DataDirectory { get; }

    /// <summary>Full path of the SQLite database file.</summary>
    public string DatabasePath => Path.Combine(DataDirectory, "sds.db");

    /// <summary>Staging folder for uploads and exports.</summary>
    public string Staging => Path.Combine(DataDirectory, "Staging");

    /// <summary>
    /// Folder of the temporary exports (Staging\exports\&lt;guid&gt;\&lt;image&gt;.wim) that clients
    /// download through GET /api/jobs/{id}/file. It is emptied when the server starts and stops.
    /// </summary>
    public string ExportStaging => Path.Combine(Staging, "exports");

    /// <summary>Scratch folder handed to wimgapi.</summary>
    public string WimTemp => Path.Combine(DataDirectory, "Staging", "wimtemp");

    /// <summary>Log folder.</summary>
    public string Logs => Path.Combine(DataDirectory, "Logs");

    /// <summary>Host name used in UNC paths.</summary>
    public string ServerName { get; }

    /// <summary>Name of the SMB share of IMAGES.</summary>
    public string ImagesShareName => _options.ImagesShareName;

    /// <summary>Name of the SMB share of DRIVERS.</summary>
    public string DriversShareName => _options.DriversShareName;

    /// <summary>UNC path of the IMAGES share, for example \\server\images.</summary>
    public string ImagesUnc => $"\\\\{ServerName}\\{ImagesShareName}";

    /// <summary>UNC path of the DRIVERS share, for example \\server\drivers.</summary>
    public string DriversUnc => $"\\\\{ServerName}\\{DriversShareName}";

    /// <summary>Folder of an image group.</summary>
    public string GroupDirectory(string groupName) => Path.Combine(Images, groupName);

    /// <summary>Res.RWM of an image group.</summary>
    public string ResourceFile(string groupName) => Path.Combine(GroupDirectory(groupName), WdsImageFormat.ResourceFileName);

    /// <summary>Metadata WIM of an image.</summary>
    public string ImageFile(string groupName, string fileName) => Path.Combine(GroupDirectory(groupName), fileName);

    /// <summary>
    /// WDS per image unattend location: IMAGES\&lt;Group&gt;\&lt;ImageFileBaseName&gt;\Unattend\ImageUnattend.xml.
    /// </summary>
    public string ImageUnattendFile(string groupName, string fileName) =>
        Path.Combine(GroupDirectory(groupName), Path.GetFileNameWithoutExtension(fileName), "Unattend", "ImageUnattend.xml");

    /// <summary>Folder of a driver package.</summary>
    public string DriverDirectory(string folderName) => Path.Combine(Drivers, folderName);

    /// <summary>
    /// Builds the driver package folder name &lt;drvname&gt;_&lt;arch&gt;_{GUID}.
    /// </summary>
    public static string DriverFolderName(string infFileName, string architecture, Guid id) =>
        $"{SanitizeFileName(Path.GetFileNameWithoutExtension(infFileName)).ToLowerInvariant()}_{architecture}_{id.ToString("B").ToUpperInvariant()}";

    /// <summary>
    /// Parses a folder name produced by <see cref="DriverFolderName"/>. Returns false when the
    /// name does not follow the pattern.
    /// </summary>
    public static bool TryParseDriverFolderName(string folderName, out string driverName, out string architecture, out Guid id)
    {
        driverName = architecture = string.Empty;
        id = Guid.Empty;

        // The GUID is the trailing {...} part; the architecture is the token before it.
        var guidStart = folderName.LastIndexOf("_{", StringComparison.Ordinal);
        if (guidStart <= 0 || !Guid.TryParse(folderName[(guidStart + 1)..], out id))
        {
            return false;
        }

        var head = folderName[..guidStart];
        var archStart = head.LastIndexOf('_');
        if (archStart <= 0)
        {
            return false;
        }

        driverName = head[..archStart];
        architecture = head[(archStart + 1)..];
        return true;
    }

    /// <summary>
    /// UNC path of a file or folder under IMAGES or DRIVERS, for example
    /// IMAGES\Win11\install.wim becomes \\server\images\Win11\install.wim. Each of the two
    /// folders is its own share, so the folder name itself is not part of the UNC path.
    /// </summary>
    /// <exception cref="InvalidOperationException">The path is outside IMAGES and DRIVERS (not shared).</exception>
    public string ToUnc(string fullPath)
    {
        if (TryRelative(Images, fullPath, out var imagesRelative))
        {
            return Combine(ImagesUnc, imagesRelative);
        }

        if (TryRelative(Drivers, fullPath, out var driversRelative))
        {
            return Combine(DriversUnc, driversRelative);
        }

        throw new InvalidOperationException($"'{fullPath}' is not under a shared folder (IMAGES or DRIVERS).");

        static string Combine(string unc, string relative) => relative == "." ? unc : unc + "\\" + relative;
    }

    /// <summary>
    /// Returns the path of <paramref name="fullPath"/> relative to <paramref name="baseDirectory"/>
    /// when it is the folder itself or inside it.
    /// </summary>
    private static bool TryRelative(string baseDirectory, string fullPath, out string relative)
    {
        relative = Path.GetRelativePath(baseDirectory, Path.GetFullPath(fullPath));

        // GetRelativePath returns the input unchanged (rooted) for another volume, and a path
        // starting with ".." when the target is outside the base folder.
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith("..\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates a name used as a folder or file name (image group, image file). Rejects
    /// path separators, reserved device names and invalid characters.
    /// </summary>
    public static string ValidateName(string? name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SdsValidationException($"{what} is required.");
        }

        name = name.Trim();
        if (name.Length > 128 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.EndsWith('.'))
        {
            throw new SdsValidationException($"{what} '{name}' contains characters that are not allowed in a file name.");
        }

        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
        {
            throw new SdsValidationException($"{what} '{name}' is a reserved device name.");
        }

        return name;
    }

    /// <summary>Replaces characters that cannot appear in a file name with underscores.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
    }

    private static string GetDnsName()
    {
        try
        {
            return Dns.GetHostEntry(string.Empty).HostName;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return Environment.MachineName;
        }
    }
}
