using System.Globalization;
using System.Text;

namespace Sds.Core.Computers;

/// <summary>Values available to the naming policy.</summary>
/// <param name="First">First name of the user.</param>
/// <param name="Last">Last name of the user.</param>
/// <param name="Username">User name (domain prefix or UPN suffix removed).</param>
/// <param name="Mac">MAC address of the client (12 hex digits).</param>
public sealed record NamingValues(string? First, string? Last, string? Username, string? Mac);

/// <summary>
/// Implements the WDS client naming policy language.
/// </summary>
/// <remarks>
/// Supported tokens (same syntax as WDS):
/// <list type="bullet">
///   <item><description><c>%First</c>, <c>%Last</c>, <c>%Username</c>, <c>%MAC</c>: a value; an
///   optional number in front limits its length, for example <c>%61Username</c> or <c>%3First</c>.</description></item>
///   <item><description><c>%#</c>: an incrementing number; <c>%n#</c> uses at least n digits
///   without padding, <c>%0n#</c> pads with zeros to n digits (for example <c>%03#</c> gives 001).</description></item>
///   <item><description><c>%%</c>: a literal percent sign.</description></item>
/// </list>
/// The result is limited to 15 characters (NetBIOS). When it is too long, text before the
/// number is shortened so the number stays intact.
/// </remarks>
public static class NamingPolicy
{
    private const int MaxLength = 15;
    private const int MaxAttempts = 100000;

    private abstract record Token;

    private sealed record Literal(string Text) : Token;

    private sealed record Variable(string Name, int? Limit) : Token;

    private sealed record Counter(int Digits, bool Pad) : Token;

    /// <summary>Throws <see cref="SdsValidationException"/> when the policy cannot be parsed.</summary>
    public static void Validate(string policy)
    {
        var tokens = Parse(policy);
        if (!tokens.Any(t => t is Variable or Counter))
        {
            throw new SdsValidationException("The naming policy must contain at least one variable or %#.");
        }
    }

    /// <summary>
    /// Generates a name. <paramref name="isTaken"/> is called for each candidate; the counter
    /// is incremented until a free name is found. Without a counter token a taken name is an error.
    /// </summary>
    public static async Task<string> GenerateAsync(string policy, NamingValues values, Func<string, Task<bool>> isTaken)
    {
        var tokens = Parse(policy);
        var hasCounter = tokens.Any(t => t is Counter);

        for (int n = 1; n <= (hasCounter ? MaxAttempts : 1); n++)
        {
            var candidate = Render(tokens, values, n);
            if (candidate.Length == 0)
            {
                throw new SdsValidationException("The naming policy produced an empty name; provide user or MAC information.");
            }

            if (!await isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new SdsConflictException("No free computer name could be generated with the naming policy.");
    }

    private static string Render(List<Token> tokens, NamingValues values, int counter)
    {
        // Render each token separately so the length limit can be applied around the counter.
        var parts = new List<(string Text, bool IsCounter)>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case Literal l:
                    parts.Add((Clean(l.Text), false));
                    break;
                case Variable v:
                    var value = Clean(v.Name switch
                    {
                        "FIRST" => values.First,
                        "LAST" => values.Last,
                        "USERNAME" => StripDomain(values.Username),
                        "MAC" => values.Mac,
                        _ => null,
                    } ?? string.Empty);
                    parts.Add((v.Limit is int limit && value.Length > limit ? value[..limit] : value, false));
                    break;
                case Counter c:
                    var number = counter.ToString(CultureInfo.InvariantCulture);
                    parts.Add((c.Pad ? number.PadLeft(c.Digits, '0') : number, true));
                    break;
            }
        }

        // Shorten non counter parts from right to left until the name fits in 15 characters.
        var overflow = parts.Sum(p => p.Text.Length) - MaxLength;
        for (int i = parts.Count - 1; i >= 0 && overflow > 0; i--)
        {
            if (parts[i].IsCounter)
            {
                continue;
            }

            var cut = Math.Min(overflow, parts[i].Text.Length);
            parts[i] = (parts[i].Text[..^cut], false);
            overflow -= cut;
        }

        return string.Concat(parts.Select(p => p.Text)).ToUpperInvariant().Trim('-');
    }

    private static List<Token> Parse(string policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            throw new SdsValidationException("The naming policy is empty.");
        }

        var tokens = new List<Token>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < policy.Length)
        {
            if (policy[i] != '%')
            {
                literal.Append(policy[i++]);
                continue;
            }

            i++; // Skip '%'.
            if (i < policy.Length && policy[i] == '%')
            {
                literal.Append('%');
                i++;
                continue;
            }

            // Optional zero padding flag and digit count.
            bool pad = i < policy.Length && policy[i] == '0';
            int start = i;
            while (i < policy.Length && char.IsDigit(policy[i]))
            {
                i++;
            }

            int? number = i > start ? int.Parse(policy[start..i], CultureInfo.InvariantCulture) : null;

            if (literal.Length > 0)
            {
                tokens.Add(new Literal(literal.ToString()));
                literal.Clear();
            }

            if (i < policy.Length && policy[i] == '#')
            {
                tokens.Add(new Counter(number ?? 1, pad));
                i++;
                continue;
            }

            var name = new[] { "Username", "First", "Last", "MAC" }
                .FirstOrDefault(n => string.Compare(policy, i, n, 0, n.Length, StringComparison.OrdinalIgnoreCase) == 0);
            if (name is null)
            {
                throw new SdsValidationException($"Unknown naming policy token at position {start}: '{policy[(start - 1)..]}'.");
            }

            tokens.Add(new Variable(name.ToUpperInvariant(), number));
            i += name.Length;
        }

        if (literal.Length > 0)
        {
            tokens.Add(new Literal(literal.ToString()));
        }

        return tokens;
    }

    /// <summary>Keeps only characters valid in a computer name.</summary>
    private static string Clean(string value) => new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());

    private static string? StripDomain(string? user)
    {
        if (user is null)
        {
            return null;
        }

        var slash = user.LastIndexOf('\\');
        if (slash >= 0)
        {
            user = user[(slash + 1)..];
        }

        var at = user.IndexOf('@');
        return at > 0 ? user[..at] : user;
    }
}
