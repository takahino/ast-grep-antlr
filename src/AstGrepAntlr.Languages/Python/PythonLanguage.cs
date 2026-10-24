using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Python;

namespace AstGrepAntlr.Languages;

public sealed class PythonLanguage : IAntlrLanguage
{
    public string Id => "python";
    public IReadOnlyList<string> Extensions { get; } = [".py"];
    public string StartRule => "file_input";
    public IReadOnlyList<string> PatternRules { get; } =
    ["atom", "expr", "test", "simple_stmt", "compound_stmt", "funcdef", "stmt", "file_input"];
    public string MetaVarPlaceholderTemplate => "__MV{0}__";
    public IReadOnlyList<string> MetaVarVariants { get; } =
    ["{0}", "{0}\n", "", "pass", "x = {0}", "def {0}():\n    pass\n"];
    public bool IgnoreCase => false;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => Python3Parser.ruleNames;
    public IVocabulary Vocabulary => Python3Lexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new Python3Lexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new Python3Parser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (Python3Parser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "file_input" => p.file_input(),
            "stmt" => p.stmt(),
            "simple_stmt" => p.simple_stmt(),
            "compound_stmt" => p.compound_stmt(),
            "funcdef" => p.funcdef(),
            "test" => p.test(),
            "expr" => p.expr(),
            "atom" => p.atom(),
            _ => throw new ArgumentException($"Unknown Python rule '{ruleName}'."),
        };
    }

    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
        if (token.Type < 0)
        {
            return false;
        }

        var symbolic = vocabulary.GetSymbolicName(token.Type);
        var literal = vocabulary.GetLiteralName(token.Type);
        if (
            !string.IsNullOrEmpty(literal)
            && literal.StartsWith('\'')
            && string.Equals(symbolic, literal.Trim('\''), StringComparison.OrdinalIgnoreCase)
        )
        {
            var inner = literal.Trim('\'');
            return inner.Length > 1 && char.IsLetter(inner[0]);
        }

        if (!string.IsNullOrEmpty(literal) && literal.StartsWith('\'') && literal.Length <= 4)
        {
            var inner = literal.Trim('\'');
            if (inner.Length <= 2 && !inner.All(char.IsLetter))
            {
                return false;
            }
        }

        return !string.IsNullOrEmpty(symbolic);
    }
}
