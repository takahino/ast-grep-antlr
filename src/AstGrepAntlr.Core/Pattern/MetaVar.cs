using System.Text.RegularExpressions;

namespace AstGrepAntlr.Pattern;

public static partial class MetaVar
{
    // $A, $_, $$$, $$$ARGS, ${A:expression}
    [GeneratedRegex(
        @"\$\$\$([A-Z_][A-Z0-9_]*)?|\$\{([A-Za-z_][A-Za-z0-9_]*):([A-Za-z_][A-Za-z0-9_]*)\}|\$([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex MetaVarRegex();

    public sealed record Occurrence(int Start, int Length, string Name, bool IsMulti, string? TypedRule);

    public static IReadOnlyList<Occurrence> FindAll(string pattern)
    {
        var list = new List<Occurrence>();
        foreach (Match m in MetaVarRegex().Matches(pattern))
        {
            if (m.Value.StartsWith("$$$", StringComparison.Ordinal))
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
                list.Add(new Occurrence(m.Index, m.Length, name, IsMulti: true, TypedRule: null));
            }
            else if (m.Groups[2].Success)
            {
                list.Add(
                    new Occurrence(m.Index, m.Length, m.Groups[2].Value, IsMulti: false, TypedRule: m.Groups[3].Value)
                );
            }
            else
            {
                list.Add(new Occurrence(m.Index, m.Length, m.Groups[4].Value, IsMulti: false, TypedRule: null));
            }
        }

        return list;
    }

    public static string ReplaceWithPlaceholders(
        string pattern,
        string template,
        out IReadOnlyDictionary<string, Occurrence> byPlaceholder
    )
    {
        var map = new Dictionary<string, Occurrence>(StringComparer.Ordinal);
        var occurrences = FindAll(pattern);
        if (occurrences.Count == 0)
        {
            byPlaceholder = map;
            return pattern;
        }

        // Replace from end so indexes stay valid.
        var chars = pattern.ToCharArray();
        var result = pattern;
        for (var i = occurrences.Count - 1; i >= 0; i--)
        {
            var occ = occurrences[i];
            var placeholder = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, i);
            map[placeholder] = occ;
            result = string.Concat(result.AsSpan(0, occ.Start), placeholder, result.AsSpan(occ.Start + occ.Length));
        }

        byPlaceholder = map;
        return result;
    }
}
