using AstGrepAntlr.Language;

namespace AstGrepAntlr.Parsing;

public sealed record ParseResult(
    Tree.SyntaxTree Tree,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    string EntryRule,
    bool UsedLlFallback
);

/// <summary>Facade over <see cref="ILanguage.Parse"/> (kept for call-site compatibility).</summary>
public sealed class TwoStageParser
{
    public ParseResult Parse(ILanguage language, string source, LanguageParseOptions? options = null) =>
        language.Parse(source, options);
}
