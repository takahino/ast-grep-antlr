using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.PostgreSql;

namespace AstGrepAntlr.Languages;

public sealed class PostgreSqlLanguage : IAntlrLanguage
{
    public string Id => "postgresql";
    public IReadOnlyList<string> Extensions { get; } = [".sql"];
    public string StartRule => "root";
    public IReadOnlyList<string> PatternRules { get; } = ["stmt", "selectstmt", "a_expr", "root"];

    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";

    // `{0} = 1` lets a metavariable stand for a whole predicate (`WHERE $C`) or assignment (`SET $$$`).
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0} = 1", "{0};", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(60);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => PostgreSQLParser.ruleNames;
    public IVocabulary Vocabulary => PostgreSQLLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new PostgreSQLLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new PostgreSQLParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (PostgreSQLParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "stmt" => p.stmt(),
            "selectstmt" => p.selectstmt(),
            "a_expr" => p.a_expr(),
            "root" => p.root(),
            _ => throw new ArgumentException($"Unknown PostgreSQL rule '{ruleName}'."),
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
