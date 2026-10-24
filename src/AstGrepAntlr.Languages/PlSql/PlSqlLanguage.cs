using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.PlSql;

namespace AstGrepAntlr.Languages;

public sealed class PlSqlLanguage : IAntlrLanguage
{
    public string Id => "plsql";
    public IReadOnlyList<string> Extensions { get; } = [".pls", ".pkb", ".pks", ".sql"];
    public string StartRule => "sql_script";
    public IReadOnlyList<string> PatternRules { get; } =
    ["atom", "expression", "general_element", "query_block", "select_statement", "sql_script"];

    // Must start with a letter: PlSql REGULAR_ID is SIMPLE_LETTER (…|'_'|…)*
    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0};", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(60);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => PlSqlParser.ruleNames;
    public IVocabulary Vocabulary => PlSqlLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new PlSqlLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new PlSqlParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (PlSqlParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "sql_script" => p.sql_script(),
            "select_statement" => p.select_statement(),
            "query_block" => p.query_block(),
            "expression" => p.expression(),
            "atom" => p.atom(),
            "general_element" => p.general_element(),
            _ => throw new ArgumentException($"Unknown PL/SQL rule '{ruleName}'."),
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
