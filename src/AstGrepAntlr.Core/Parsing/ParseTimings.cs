namespace AstGrepAntlr.Parsing;

/// <summary>
/// Per-phase parse totals. Each <see cref="Language.ILanguage.Parse"/> call adds to these fields.
/// Not thread-safe; intended for single-threaded micro-benchmarks.
/// </summary>
public sealed class ParseTimings
{
    /// <summary>ANTLR lexer / token fill.</summary>
    public TimeSpan Lex { get; set; }

    /// <summary>Native parse (tree-sitter / Roslyn) or ANTLR SLL stage.</summary>
    public TimeSpan Parse { get; set; }

    /// <summary>ANTLR LL retry after SLL bail / diagnostics.</summary>
    public TimeSpan LlFallback { get; set; }

    /// <summary>Conversion into arena <see cref="Tree.SyntaxTree"/>.</summary>
    public TimeSpan Build { get; set; }

    /// <summary>Diagnostic / error walk (tree-sitter / Roslyn).</summary>
    public TimeSpan Diagnostics { get; set; }

    public void AddLex(TimeSpan value) => Lex += value;

    public void AddParse(TimeSpan value) => Parse += value;

    public void AddLlFallback(TimeSpan value) => LlFallback += value;

    public void AddBuild(TimeSpan value) => Build += value;

    public void AddDiagnostics(TimeSpan value) => Diagnostics += value;
}
