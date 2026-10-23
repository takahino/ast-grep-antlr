using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using AstGrepAntlr.Language;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Parsing;

public sealed record ParseResult(
    SyntaxTree Tree,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    string EntryRule,
    bool UsedLlFallback
);

/// <summary>SLL + Bail first, then LL + DefaultErrorStrategy recovery.</summary>
public sealed class TwoStageParser
{
    private readonly TreeBuilder _builder = new();

    public ParseResult Parse(ILanguage language, string source, LanguageParseOptions? options = null)
    {
        options ??= new LanguageParseOptions();
        var entry = options.EntryRule ?? language.StartRule;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
        if (language.ParseTimeout > TimeSpan.Zero)
        {
            cts.CancelAfter(language.ParseTimeout);
        }

        var stream = CharStreams.fromString(source);
        var lexerErrors = new CollectingErrorListener();
        var lexer = language.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexerErrors);
        var tokens = new CommonTokenStream(lexer);

        ParseResult? sll = null;
        if (options.PreferSll)
        {
            try
            {
                sll = TryParse(
                    language,
                    source,
                    tokens,
                    entry,
                    PredictionMode.SLL,
                    bail: true,
                    lexerErrors,
                    usedLl: false
                );
                if (sll.Diagnostics.Count == lexerErrors.Diagnostics.Count)
                {
                    return sll;
                }
            }
            catch (ParseCanceledException)
            {
                // fall through to LL
            }
            catch (RecognitionException)
            {
                // fall through to LL
            }
        }

        tokens.Seek(0);
        return TryParse(language, source, tokens, entry, PredictionMode.LL, bail: false, lexerErrors, usedLl: true);
    }

    private ParseResult TryParse(
        ILanguage language,
        string source,
        CommonTokenStream tokens,
        string entry,
        PredictionMode mode,
        bool bail,
        CollectingErrorListener lexerErrors,
        bool usedLl
    )
    {
        var parserErrors = new CollectingErrorListener();
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parserErrors);
        parser.Interpreter.PredictionMode = mode;
        parser.ErrorHandler = bail ? new BailErrorStrategy() : new DefaultErrorStrategy();

        var tree = language.InvokeRule(parser, entry);
        var diagnostics = lexerErrors.Diagnostics.Concat(parserErrors.Diagnostics).ToList();
        var syntaxTree = _builder.Build(language, source, tree);
        return new ParseResult(syntaxTree, diagnostics, entry, usedLl);
    }
}
