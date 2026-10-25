namespace AstGrepAntlr.Languages.TreeSitter;

/// <summary>
/// How to wrap a pattern fragment so tree-sitter can parse it (no start-rule API).
/// <c>{0}</c> is replaced with the rewritten pattern text (other braces are left alone;
/// not via string.Format, which mis-parses braces in Java/C++ wrappers).
/// </summary>
public sealed record PatternContext(string Name, string Template)
{
    private const string Slot = "{0}";

    public string Wrap(string fragment) => ApplySlot(Template, fragment);

    /// <summary>UTF-16 index of the first character of the inserted fragment inside the filled template.</summary>
    public int EmbedStart(string fragment)
    {
        var wrapped = Wrap(fragment);
        if (fragment.Length == 0)
        {
            var marker = "\uE000";
            var withMarker = Wrap(marker);
            var idx = withMarker.IndexOf(marker, StringComparison.Ordinal);
            return idx < 0 ? 0 : idx;
        }

        var at = wrapped.IndexOf(fragment, StringComparison.Ordinal);
        return at < 0 ? 0 : at;
    }

    private static string ApplySlot(string template, string value)
    {
        var idx = template.IndexOf(Slot, StringComparison.Ordinal);
        if (idx < 0)
        {
            return template;
        }

        return string.Concat(template.AsSpan(0, idx), value, template.AsSpan(idx + Slot.Length));
    }
}
