using System.Text;
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

    /// <summary>
    /// Replace metavars with per-occurrence variant templates (e.g. <c>{0}</c>, <c>{0};</c>).
    /// <paramref name="placeholderSpans"/> records the span of each full inserted variant in the
    /// rewritten source (used for outermost-node metavar marking).
    /// </summary>
    public static string ReplaceWithPlaceholders(
        string pattern,
        string template,
        IReadOnlyList<Occurrence> occurrences,
        IReadOnlyList<string> variantTemplates,
        IReadOnlyList<int> choices,
        out IReadOnlyDictionary<string, Occurrence> byPlaceholder,
        out IReadOnlyDictionary<string, (int Start, int End)> placeholderSpans
    )
    {
        var map = new Dictionary<string, Occurrence>(StringComparer.Ordinal);
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        if (occurrences.Count == 0)
        {
            byPlaceholder = map;
            placeholderSpans = spans;
            return pattern;
        }

        var sb = new StringBuilder(pattern.Length + occurrences.Count * 8);
        var cursor = 0;
        for (var i = 0; i < occurrences.Count; i++)
        {
            var occ = occurrences[i];
            if (occ.Start > cursor)
            {
                sb.Append(pattern, cursor, occ.Start - cursor);
            }

            // Apply "{0}" manually — variant templates may contain literal braces
            // (e.g. "void {0}(){}") that string.Format would reject.
            var placeholder = ApplySlot(template, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var variantIndex = i < choices.Count ? choices[i] : 0;
            if (variantIndex < 0 || variantIndex >= variantTemplates.Count)
            {
                variantIndex = 0;
            }

            var inserted = ApplySlot(variantTemplates[variantIndex], placeholder);
            var insertStart = sb.Length;
            sb.Append(inserted);
            map[placeholder] = occ;
            spans[placeholder] = (insertStart, insertStart + inserted.Length);
            cursor = occ.Start + occ.Length;
        }

        if (cursor < pattern.Length)
        {
            sb.Append(pattern, cursor, pattern.Length - cursor);
        }

        byPlaceholder = map;
        placeholderSpans = spans;
        return sb.ToString();
    }

    /// <summary>Replace the first <c>{0}</c> slot; leave other braces untouched.</summary>
    private static string ApplySlot(string template, string value)
    {
        const string slot = "{0}";
        var idx = template.IndexOf(slot, StringComparison.Ordinal);
        if (idx < 0)
        {
            return template;
        }

        return string.Concat(template.AsSpan(0, idx), value, template.AsSpan(idx + slot.Length));
    }

    /// <summary>Legacy helper: apply a single variant template to every metavar.</summary>
    public static string ReplaceWithPlaceholders(
        string pattern,
        string template,
        string variantTemplate,
        out IReadOnlyDictionary<string, Occurrence> byPlaceholder,
        out IReadOnlyDictionary<string, int> placeholderStarts
    )
    {
        var occurrences = FindAll(pattern);
        var choices = new int[occurrences.Count];
        var rewritten = ReplaceWithPlaceholders(
            pattern,
            template,
            occurrences,
            [variantTemplate],
            choices,
            out byPlaceholder,
            out var spans
        );
        placeholderStarts = spans.ToDictionary(kv => kv.Key, kv => kv.Value.Start, StringComparer.Ordinal);
        return rewritten;
    }
}
