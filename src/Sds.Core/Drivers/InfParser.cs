using System.Globalization;
using System.Text;

namespace Sds.Core.Drivers;

/// <summary>One models entry: a device description and the hardware ids it installs on.</summary>
/// <param name="Description">Device description (strings resolved).</param>
/// <param name="HardwareIds">Hardware id followed by compatible ids, upper case.</param>
public sealed record InfModel(string Description, IReadOnlyList<string> HardwareIds);

/// <summary>A file referenced by the INF, relative to the INF folder.</summary>
/// <param name="RelativePath">Path relative to the folder containing the INF.</param>
public sealed record InfSourceFile(string RelativePath);

/// <summary>Everything SDS needs to know about a driver INF.</summary>
public sealed record InfInfo
{
    /// <summary>Device setup class.</summary>
    public string? ClassName { get; init; }

    /// <summary>Device setup class GUID.</summary>
    public Guid? ClassGuid { get; init; }

    /// <summary>Provider.</summary>
    public string? Provider { get; init; }

    /// <summary>Driver date.</summary>
    public DateOnly? DriverDate { get; init; }

    /// <summary>Driver version.</summary>
    public string? Version { get; init; }

    /// <summary>Catalog file per architecture ("" key = undecorated CatalogFile).</summary>
    public IReadOnlyDictionary<string, string> CatalogFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>Models per architecture (x86, amd64, arm64, arm, ia64).</summary>
    public IReadOnlyDictionary<string, List<InfModel>> ModelsByArchitecture { get; init; } = new Dictionary<string, List<InfModel>>();

    /// <summary>Source files per architecture; empty when the INF has no SourceDisksFiles section.</summary>
    public IReadOnlyDictionary<string, List<InfSourceFile>> FilesByArchitecture { get; init; } = new Dictionary<string, List<InfSourceFile>>();

    /// <summary>Catalog for an architecture (decorated first, then undecorated).</summary>
    public string? CatalogFor(string arch) =>
        CatalogFiles.TryGetValue(arch, out var c) ? c : CatalogFiles.TryGetValue(string.Empty, out var d) ? d : null;
}

/// <summary>
/// Minimal Windows INF parser covering what a driver store needs: [Version], [Manufacturer]
/// with platform decorations, models sections, [Strings] substitution and the
/// SourceDisksNames / SourceDisksFiles sections used to find the package files.
/// </summary>
/// <remarks>
/// Platform decorations follow the INF rules: NTamd64, NTx86, NTarm64, NTarm and NTia64,
/// optionally followed by OS version fields (NTamd64.10.0...1). An undecorated models section
/// or a plain "NT" decoration only applies to x86, because 64 bit Windows ignores undecorated
/// models sections.
/// </remarks>
public static class InfParser
{
    private static readonly string[] Architectures = ["amd64", "x86", "arm64", "arm", "ia64"];

    /// <summary>Parses an INF file from disk (UTF-16 or ANSI/UTF-8 encodings).</summary>
    public static InfInfo ParseFile(string path) => Parse(ReadText(path));

    /// <summary>Parses INF text.</summary>
    public static InfInfo Parse(string text)
    {
        var sections = ReadSections(text);
        var strings = ReadStrings(sections);
        string Resolve(string value) => Substitute(value, strings);

        // [Version]
        var version = Pairs(sections, "Version").ToDictionary(p => p.Key, p => Resolve(p.Value), StringComparer.OrdinalIgnoreCase);
        version.TryGetValue("Class", out var className);
        Guid? classGuid = version.TryGetValue("ClassGuid", out var cg) && Guid.TryParse(cg, out var parsedGuid) ? parsedGuid : null;
        version.TryGetValue("Provider", out var provider);
        DateOnly? date = null;
        string? driverVersion = null;
        if (version.TryGetValue("DriverVer", out var driverVer))
        {
            // DriverVer = mm/dd/yyyy[,w.x.y.z]
            var parts = driverVer.Split(',', 2, StringSplitOptions.TrimEntries);
            if (DateOnly.TryParseExact(parts[0], ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                date = d;
            }

            driverVersion = parts.Length > 1 ? parts[1] : null;
        }

        var catalogs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in version)
        {
            if (key.Equals("CatalogFile", StringComparison.OrdinalIgnoreCase))
            {
                catalogs[string.Empty] = value;
            }
            else if (key.StartsWith("CatalogFile.", StringComparison.OrdinalIgnoreCase))
            {
                var arch = ArchitectureFromDecoration(key["CatalogFile.".Length..]);
                if (arch is not null)
                {
                    catalogs[arch] = value;
                }
            }
        }

        // [Manufacturer]: %Mfg% = ModelsSection[, decoration...]
        var models = new Dictionary<string, List<InfModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, value) in Pairs(sections, "Manufacturer"))
        {
            var fields = SplitFields(value);
            if (fields.Count == 0)
            {
                continue;
            }

            var baseSection = fields[0];
            var decorations = fields.Skip(1).Where(f => f.Length > 0).ToList();

            if (decorations.Count == 0)
            {
                AddModels(models, "x86", sections, baseSection, Resolve);
                continue;
            }

            foreach (var decoration in decorations)
            {
                var arch = ArchitectureFromDecoration(decoration) ?? "x86";
                AddModels(models, arch, sections, baseSection + "." + decoration, Resolve);
            }
        }

        return new InfInfo
        {
            ClassName = className,
            ClassGuid = classGuid,
            Provider = provider,
            DriverDate = date,
            Version = driverVersion,
            CatalogFiles = catalogs,
            ModelsByArchitecture = models,
            FilesByArchitecture = Architectures.ToDictionary(a => a, a => SourceFiles(sections, a, Resolve)),
        };
    }

    /// <summary>
    /// Maps a decoration such as NTamd64, NTamd64.10.0...19041 or ntx86 to an architecture,
    /// or null when the decoration has no architecture part.
    /// </summary>
    public static string? ArchitectureFromDecoration(string decoration)
    {
        var d = decoration.Trim().ToLowerInvariant();
        if (d.StartsWith("nt", StringComparison.Ordinal))
        {
            d = d[2..];
        }

        var arch = d.Split('.')[0];
        return Architectures.Contains(arch) ? arch : null;
    }

    private static void AddModels(Dictionary<string, List<InfModel>> models, string arch, Dictionary<string, List<string>> sections, string sectionName, Func<string, string> resolve)
    {
        if (!models.TryGetValue(arch, out var list))
        {
            list = [];
            models[arch] = list;
        }

        // %DeviceDesc% = InstallSection, HardwareId[, CompatibleId...]
        foreach (var (key, value) in Pairs(sections, sectionName))
        {
            var fields = SplitFields(value);
            var ids = fields.Skip(1).Select(f => resolve(f).Trim().ToUpperInvariant()).Where(f => f.Length > 0).ToList();
            if (ids.Count > 0)
            {
                list.Add(new InfModel(resolve(key), ids));
            }
        }
    }

    /// <summary>
    /// Builds the list of files referenced by the INF for one architecture. A file entry
    /// "name = diskid[,subdir]" lives in &lt;disk path&gt;\&lt;subdir&gt;\name, where the disk
    /// path is the fourth field of the matching SourceDisksNames entry.
    /// </summary>
    private static List<InfSourceFile> SourceFiles(Dictionary<string, List<string>> sections, string arch, Func<string, string> resolve)
    {
        var disks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SourceDisksNames", "SourceDisksNames." + arch })
        {
            foreach (var (key, value) in Pairs(sections, name))
            {
                var fields = SplitFields(value);
                disks[key] = fields.Count > 3 ? resolve(fields[3]).Trim('\\', '/', ' ') : string.Empty;
            }
        }

        var files = new List<InfSourceFile>();
        foreach (var name in new[] { "SourceDisksFiles", "SourceDisksFiles." + arch })
        {
            foreach (var (key, value) in Pairs(sections, name))
            {
                var fields = SplitFields(value);
                var disk = fields.Count > 0 ? fields[0] : string.Empty;
                var subdir = fields.Count > 1 ? resolve(fields[1]).Trim('\\', '/', ' ') : string.Empty;
                disks.TryGetValue(disk, out var diskPath);
                var relative = Path.Combine(diskPath ?? string.Empty, subdir, resolve(key));
                files.Add(new InfSourceFile(relative));
            }
        }

        return files;
    }

    // ------------------------------------------------------------------
    // Section: low level INF syntax
    // ------------------------------------------------------------------

    private static string ReadText(string path)
    {
        // INF files are UTF-16 LE with BOM or ANSI; detectEncodingFromByteOrderMarks handles
        // the BOM cases and Latin1 keeps ANSI bytes intact.
        using var reader = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Splits the file into sections; each section is a list of logical lines.</summary>
    private static Dictionary<string, List<string>> ReadSections(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        var pending = new StringBuilder();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = StripComment(rawLine.TrimEnd('\r')).Trim();

            // A trailing backslash continues the logical line.
            if (line.EndsWith('\\'))
            {
                pending.Append(line[..^1]);
                continue;
            }

            pending.Append(line);
            var logical = pending.ToString().Trim();
            pending.Clear();
            if (logical.Length == 0)
            {
                continue;
            }

            if (logical.StartsWith('[') && logical.EndsWith(']'))
            {
                var name = logical[1..^1].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    // Sections with the same name are merged, as SetupAPI does.
                    current = [];
                    sections[name] = current;
                }

                continue;
            }

            current?.Add(logical);
        }

        return sections;
    }

    /// <summary>Removes a ';' comment that is not inside double quotes.</summary>
    private static string StripComment(string line)
    {
        var inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ';' && !inQuotes)
            {
                return line[..i];
            }
        }

        return line;
    }

    /// <summary>Returns the key = value lines of a section (lines without '=' get an empty key).</summary>
    private static IEnumerable<(string Key, string Value)> Pairs(Dictionary<string, List<string>> sections, string name)
    {
        if (!sections.TryGetValue(name, out var lines))
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var eq = IndexOutsideQuotes(line, '=');
            if (eq < 0)
            {
                yield return (string.Empty, line);
            }
            else
            {
                yield return (Unquote(line[..eq].Trim()), line[(eq + 1)..].Trim());
            }
        }
    }

    private static Dictionary<string, string> ReadStrings(Dictionary<string, List<string>> sections)
    {
        // [Strings] first, then localized [Strings.xxxx] sections fill missing tokens only.
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sections.Keys.Where(k => k.Equals("Strings", StringComparison.OrdinalIgnoreCase))
                     .Concat(sections.Keys.Where(k => k.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var (key, value) in Pairs(sections, name))
            {
                strings.TryAdd(key, Unquote(value));
            }
        }

        return strings;
    }

    /// <summary>Replaces %token% with its [Strings] value; %% stays a literal percent.</summary>
    private static string Substitute(string value, Dictionary<string, string> strings)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < value.Length)
        {
            if (value[i] == '%')
            {
                var end = value.IndexOf('%', i + 1);
                if (end > i)
                {
                    var token = value[(i + 1)..end];
                    sb.Append(token.Length == 0 ? "%" : strings.TryGetValue(token, out var s) ? s : value[i..(end + 1)]);
                    i = end + 1;
                    continue;
                }
            }

            sb.Append(value[i++]);
        }

        return Unquote(sb.ToString());
    }

    /// <summary>Splits a value on commas outside quotes and trims/unquotes each field.</summary>
    private static List<string> SplitFields(string value)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in value)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(Unquote(sb.ToString().Trim()));
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(Unquote(sb.ToString().Trim()));
        return fields;
    }

    private static int IndexOutsideQuotes(string line, char c)
    {
        var inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == c && !inQuotes)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Replace("\"\"", "\"") : value;
}
