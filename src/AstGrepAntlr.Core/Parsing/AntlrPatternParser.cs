using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using AstGrepAntlr.Language;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Parsing;

/// <summary>Bail-mode pattern fragment parse for ANTLR backends (SLL then LL).</summary>
internal static class AntlrPatternParser
{
    public static SyntaxTree? TryParseFragment(IAntlrLanguage language, string source, string rule)
    {
        return TryParseFragmentWithMode(language, source, rule, PredictionMode.SLL)
            ?? TryParseFragmentWithMode(language, source, rule, PredictionMode.LL);
    }

    private static SyntaxTree? TryParseFragmentWithMode(
        IAntlrLanguage language,
        string source,
        string rule,
        PredictionMode mode
    )
    {
        var stream = CharStreams.fromString(source);
        var lexer = language.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        parser.ErrorHandler = new BailErrorStrategy();
        parser.Interpreter.PredictionMode = mode;
        try
        {
            var tree = language.InvokeRule(parser, rule);
            if (tokens.LA(1) != TokenConstants.EOF)
            {
                return null;
            }

            return new TreeBuilder().Build(language, source, tree);
        }
        catch (ParseCanceledException)
        {
            return null;
        }
        catch (RecognitionException)
        {
            return null;
        }
    }
}
