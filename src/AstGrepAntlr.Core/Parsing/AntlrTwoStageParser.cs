using System.Diagnostics;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using AstGrepAntlr.Language;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Parsing;

/// <summary>SLL + Bail first, then LL + DefaultErrorStrategy recovery (ANTLR backends).</summary>
internal static class AntlrTwoStageParser
{
    public static ParseResult Parse(IAntlrLanguage language, string source, LanguageParseOptions? options = null)
    {
        options ??= new LanguageParseOptions();
        var entry = options.EntryRule ?? language.StartRule;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
        if (language.ParseTimeout > TimeSpan.Zero)
        {
            cts.CancelAfter(language.ParseTimeout);
        }

        cts.Token.ThrowIfCancellationRequested();

        var timings = options.Timings;
        var stream = CharStreams.fromString(source);
        var lexerErrors = new CollectingErrorListener();
        var lexer = language.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexerErrors);
        var tokens = new CommonTokenStream(lexer);

        // Fill tokens up front so lex time is separable from parse prediction.
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            tokens.Fill();
            timings.AddLex(sw.Elapsed);
        }
        else
        {
            tokens.Fill();
        }

        cts.Token.ThrowIfCancellationRequested();

        AntlrParseAttempt? sll = null;
        if (options.PreferSll)
        {
            try
            {
                sll = TryParse(
                    language,
                    tokens,
                    entry,
                    PredictionMode.SLL,
                    bail: true,
                    lexerErrors,
                    usedLl: false,
                    timings,
                    llFallback: false,
                    cts.Token
                );
                if (sll.Diagnostics.Count == lexerErrors.Diagnostics.Count)
                {
                    return BuildResult(language, source, entry, sll, timings);
                }
            }
            catch (ParseCanceledException) when (!cts.IsCancellationRequested)
            {
                // SLL bail (not a user timeout / cancel) — fall through to LL
            }
            catch (RecognitionException) when (!cts.IsCancellationRequested)
            {
                // fall through to LL
            }
        }

        tokens.Seek(0);
        var ll = TryParse(
            language,
            tokens,
            entry,
            PredictionMode.LL,
            bail: false,
            lexerErrors,
            usedLl: true,
            timings,
            llFallback: true,
            cts.Token
        );
        return BuildResult(language, source, entry, ll, timings);
    }

    private static ParseResult BuildResult(
        IAntlrLanguage language,
        string source,
        string entry,
        AntlrParseAttempt attempt,
        ParseTimings? timings
    )
    {
        SyntaxTree syntaxTree;
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            syntaxTree = new TreeBuilder().Build(language, source, attempt.Tree);
            timings.AddBuild(sw.Elapsed);
        }
        else
        {
            syntaxTree = new TreeBuilder().Build(language, source, attempt.Tree);
        }

        return new ParseResult(syntaxTree, attempt.Diagnostics, entry, attempt.UsedLl);
    }

    private static AntlrParseAttempt TryParse(
        IAntlrLanguage language,
        CommonTokenStream tokens,
        string entry,
        PredictionMode mode,
        bool bail,
        CollectingErrorListener lexerErrors,
        bool usedLl,
        ParseTimings? timings,
        bool llFallback,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parserErrors = new CollectingErrorListener();
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parserErrors);
        parser.Interpreter.PredictionMode = mode;
        parser.ErrorHandler = bail
            ? new CancelableBailErrorStrategy(cancellationToken)
            : new CancelableDefaultErrorStrategy(cancellationToken);

        ParserRuleContext tree;
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            tree = language.InvokeRule(parser, entry);
            if (llFallback)
            {
                timings.AddLlFallback(sw.Elapsed);
            }
            else
            {
                timings.AddParse(sw.Elapsed);
            }
        }
        else
        {
            tree = language.InvokeRule(parser, entry);
        }

        var diagnostics = lexerErrors.Diagnostics.Concat(parserErrors.Diagnostics).ToList();
        return new AntlrParseAttempt(tree, diagnostics, usedLl);
    }

    private sealed record AntlrParseAttempt(IParseTree Tree, IReadOnlyList<ParseDiagnostic> Diagnostics, bool UsedLl);
}
