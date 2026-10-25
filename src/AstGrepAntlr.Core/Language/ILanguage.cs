using AstGrepAntlr.Parsing;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Language;

/// <summary>Language front-end: parse source/patterns into arena <see cref="SyntaxTree"/>s.</summary>
public interface ILanguage
{
    string Id { get; }
    IReadOnlyList<string> Extensions { get; }
    string StartRule { get; }
    IReadOnlyList<string> PatternRules { get; }
    string MetaVarPlaceholderTemplate { get; }

    /// <summary>Placeholder shapes tried when a pattern fails to parse; <c>{0}</c> is the per-slot placeholder text.</summary>
    IReadOnlyList<string> MetaVarVariants => ["{0}"];
    bool IgnoreCase { get; }
    TimeSpan ParseTimeout { get; }
    long MaxFileBytes { get; }

    /// <summary>Node kind names this language may emit (for MCP describe / tooling).</summary>
    IReadOnlyList<string> RuleNames { get; }

    ParseResult Parse(string source, LanguageParseOptions? options = null);

    /// <summary>
    /// Parse a pattern fragment with the given entry rule. Returns null when the fragment
    /// does not consume the full input or produces parse errors.
    /// </summary>
    SyntaxTree? TryParseFragment(string source, string rule);
}

public sealed class LanguageParseOptions
{
    public bool PreferSll { get; init; } = true;
    public string? EntryRule { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Optional accumulator for per-phase parse timings. When null, parsers skip Stopwatch work.
    /// </summary>
    public ParseTimings? Timings { get; init; }
}
