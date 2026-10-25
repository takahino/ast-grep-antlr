using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.MySql;

namespace AstGrepAntlr.Languages;

public sealed class MySqlLanguage : IAntlrLanguage
{
    public string Id => "mysql";
    public IReadOnlyList<string> Extensions { get; } = [".sql"];
    public string StartRule => "root";
    public IReadOnlyList<string> PatternRules { get; } =
    ["sqlStatement", "selectStatement", "querySpecification", "expression", "fullColumnName", "uid", "root"];

    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";

    // `{0} = 1` lets a metavariable stand for a whole predicate (`WHERE $C`) or assignment (`SET $$$`).
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0} = 1", "{0};", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(60);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => MySqlParser.ruleNames;
    public IVocabulary Vocabulary => MySqlLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new MySqlLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new MySqlParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (MySqlParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "sqlstatement" => p.sqlStatement(),
            "selectstatement" => p.selectStatement(),
            "queryspecification" => p.querySpecification(),
            "expression" => p.expression(),
            "fullcolumnname" => p.fullColumnName(),
            "uid" => p.uid(),
            "root" => p.root(),
            _ => throw new ArgumentException($"Unknown MySQL rule '{ruleName}'."),
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
        if (!string.IsNullOrEmpty(literal) && literal.StartsWith('\''))
        {
            var inner = literal.Trim('\'');
            if (inner.Length <= 2 && !inner.All(char.IsLetter))
            {
                return false;
            }

            return inner.Length > 1 && char.IsLetter(inner[0]);
        }

        return !string.IsNullOrEmpty(symbolic);
    }
}
