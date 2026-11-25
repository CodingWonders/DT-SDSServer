using System.Text.RegularExpressions;

namespace Sds.Core.Util;

/// <summary>Normalisation and validation of the identifiers used for clients.</summary>
public static partial class Identifiers
{
    /// <summary>
    /// Normalises a MAC address to 12 upper case hex digits without separators
    /// (AABBCCDDEEFF). Accepts colons, dashes, dots or no separator. Returns null for null
    /// or empty input; throws for malformed input.
    /// </summary>
    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        var separatorsOnly = mac.Where(c => !Uri.IsHexDigit(c)).All(c => c is ':' or '-' or '.' or ' ');
        if (hex.Length != 12 || !separatorsOnly)
        {
            throw new SdsValidationException($"'{mac}' is not a valid MAC address.");
        }

        return hex;
    }

    /// <summary>
    /// Parses an SMBIOS UUID. Returns null for null, empty, all zero or all FF values, which
    /// firmware reports when no UUID is set (WDS ignores those as well).
    /// </summary>
    public static Guid? NormalizeGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        if (!Guid.TryParse(guid, out var parsed))
        {
            throw new SdsValidationException($"'{guid}' is not a valid GUID.");
        }

        return IsMeaningless(parsed) ? null : parsed;
    }

    /// <summary>Same as <see cref="NormalizeGuid(string?)"/> for an already parsed value.</summary>
    public static Guid? NormalizeGuid(Guid? guid) => guid is null || IsMeaningless(guid.Value) ? null : guid;

    /// <summary>
    /// Validates a computer name: 1 to 15 characters (NetBIOS limit), letters, digits and
    /// hyphens, not only digits.
    /// </summary>
    public static string ValidateComputerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SdsValidationException("Computer name is required.");
        }

        name = name.Trim().ToUpperInvariant();
        if (!ComputerNameRegex().IsMatch(name) || name.All(char.IsDigit))
        {
            throw new SdsValidationException($"'{name}' is not a valid computer name (1 to 15 letters, digits or hyphens).");
        }

        return name;
    }

    /// <summary>Normalises an architecture name to x86, amd64, arm64, arm or ia64.</summary>
    public static string? NormalizeArchitecture(string? arch) => arch?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "x86" or "i386" or "ia32" => "x86",
        "x64" or "amd64" or "x86_64" => "amd64",
        "arm64" or "aarch64" => "arm64",
        "arm" => "arm",
        "ia64" => "ia64",
        var other => throw new SdsValidationException($"Unknown architecture '{other}'."),
    };

    private static bool IsMeaningless(Guid g) =>
        g == Guid.Empty || g == Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");

    [GeneratedRegex("^[A-Z0-9-]{1,15}$")]
    private static partial Regex ComputerNameRegex();
}
