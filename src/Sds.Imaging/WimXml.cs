using System.Globalization;
using System.Xml.Linq;

namespace Sds.Imaging;

/// <summary>
/// Helpers to read and modify the XML data block stored in every WIM file.
/// </summary>
/// <remarks>
/// The XML has the shape
/// <c>&lt;WIM&gt;&lt;IMAGE INDEX="1"&gt;&lt;NAME/&gt;&lt;WINDOWS&gt;&lt;ARCH/&gt;...&lt;/WINDOWS&gt;&lt;/IMAGE&gt;&lt;/WIM&gt;</c>.
/// When queried for a single image handle wimgapi returns only the IMAGE element.
/// </remarks>
public static class WimXml
{
    /// <summary>Parses the XML of a whole WIM file into one record per image.</summary>
    public static IReadOnlyList<WimImageInfo> ParseWim(string xml)
    {
        var root = XElement.Parse(xml);
        return root.Elements("IMAGE").Select(ParseImage).ToList();
    }

    /// <summary>Parses a single IMAGE element (as text).</summary>
    public static WimImageInfo ParseImage(string imageXml) => ParseImage(XElement.Parse(imageXml));

    /// <summary>Parses a single IMAGE element.</summary>
    public static WimImageInfo ParseImage(XElement image)
    {
        var windows = image.Element("WINDOWS");
        var version = windows?.Element("VERSION");
        var languages = windows?.Element("LANGUAGES");

        // Build the version string only when the VERSION block exists.
        string? versionText = null;
        if (version is not null)
        {
            versionText = string.Join('.',
                Text(version, "MAJOR") ?? "0",
                Text(version, "MINOR") ?? "0",
                Text(version, "BUILD") ?? "0",
                Text(version, "SPBUILD") ?? "0");
        }

        return new WimImageInfo
        {
            Index = int.TryParse((string?)image.Attribute("INDEX"), out var idx) ? idx : 0,
            Name = Text(image, "NAME") ?? string.Empty,
            Description = Text(image, "DESCRIPTION"),
            DisplayName = Text(image, "DISPLAYNAME"),
            DisplayDescription = Text(image, "DISPLAYDESCRIPTION"),
            Flags = Text(image, "FLAGS"),
            Architecture = MapArchitecture(Text(windows, "ARCH")),
            ProductName = Text(windows, "PRODUCTNAME"),
            EditionId = Text(windows, "EDITIONID"),
            InstallationType = Text(windows, "INSTALLATIONTYPE"),
            ProductType = Text(windows, "PRODUCTTYPE"),
            Hal = Text(windows, "HAL"),
            Languages = languages?.Elements("LANGUAGE").Select(l => l.Value).ToList() ?? [],
            DefaultLanguage = Text(languages, "DEFAULT"),
            Version = versionText,
            ServicePackLevel = int.TryParse(Text(version, "SPLEVEL"), out var sp) ? sp : 0,
            TotalBytes = long.TryParse(Text(image, "TOTALBYTES"), out var tb) ? tb : 0,
            DirectoryCount = long.TryParse(Text(image, "DIRCOUNT"), out var dc) ? dc : 0,
            FileCount = long.TryParse(Text(image, "FILECOUNT"), out var fc) ? fc : 0,
            CreationTimeUtc = ParseFileTime(image.Element("CREATIONTIME")),
            LastModificationTimeUtc = ParseFileTime(image.Element("LASTMODIFICATIONTIME")),
        };
    }

    /// <summary>
    /// Returns a copy of an IMAGE element with NAME, DESCRIPTION, DISPLAYNAME and
    /// DISPLAYDESCRIPTION replaced. A null argument leaves the element unchanged,
    /// an empty string removes it.
    /// </summary>
    public static string UpdateImageText(string imageXml, string? name, string? description, string? displayName = null, string? displayDescription = null)
    {
        var image = XElement.Parse(imageXml);
        SetOrRemove(image, "NAME", name);
        SetOrRemove(image, "DESCRIPTION", description);
        SetOrRemove(image, "DISPLAYNAME", displayName);
        SetOrRemove(image, "DISPLAYDESCRIPTION", displayDescription);
        return image.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Converts the numeric ARCH value of the WIM XML (PROCESSOR_ARCHITECTURE_*) to the
    /// names WDS uses: 0 = x86, 5 = arm, 6 = ia64, 9 = amd64 (x64), 12 = arm64.
    /// </summary>
    public static string MapArchitecture(string? arch) => arch switch
    {
        "0" => "x86",
        "5" => "arm",
        "6" => "ia64",
        "9" => "amd64",
        "12" => "arm64",
        _ => "unknown",
    };

    private static void SetOrRemove(XElement parent, string name, string? value)
    {
        if (value is null)
        {
            return; // Caller does not want to touch this element.
        }

        var element = parent.Element(name);
        if (value.Length == 0)
        {
            element?.Remove();
        }
        else if (element is null)
        {
            parent.Add(new XElement(name, value));
        }
        else
        {
            element.Value = value;
        }
    }

    private static string? Text(XElement? parent, string name)
    {
        var value = parent?.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// WIM XML stores times as two hexadecimal DWORDs (HIGHPART, LOWPART) of a FILETIME.
    /// </summary>
    private static DateTime? ParseFileTime(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        static uint Hex(string? value) =>
            value is null ? 0 : uint.Parse(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        try
        {
            long fileTime = ((long)Hex(element.Element("HIGHPART")?.Value) << 32) | Hex(element.Element("LOWPART")?.Value);
            return fileTime <= 0 ? null : DateTime.FromFileTimeUtc(fileTime);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
