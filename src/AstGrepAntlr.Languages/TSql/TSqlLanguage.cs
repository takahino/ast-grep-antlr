using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.TSql;

namespace AstGrepAntlr.Languages;

public sealed class TSqlLanguage : IAntlrLanguage
{
    public string Id => "tsql";
    public IReadOnlyList<string> Extensions { get; } = [".sql"];
    public string StartRule => "tsql_file";
    public IReadOnlyList<string> PatternRules { get; } =
    [
        "sql_clauses",
        "select_statement",
        "query_specification",
        "search_condition",
        "expression",
        "full_column_name",
        "id_",
        "tsql_file",
    ];

    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";

    // `{0} = 1` lets a metavariable stand for a whole predicate (`WHERE $C`) or assignment (`SET $$$`).
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0} = 1", "{0};", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(60);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => TSqlParser.ruleNames;
    public IVocabulary Vocabulary => TSqlLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new TSqlLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new TSqlParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (TSqlParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "sql_clauses" => p.sql_clauses(),
            "select_statement" => p.select_statement(),
            "query_specification" => p.query_specification(),
            "search_condition" => p.search_condition(),
            "expression" => p.expression(),
            "full_column_name" => p.full_column_name(),
            "id_" => p.id_(),
            "tsql_file" => p.tsql_file(),
            _ => throw new ArgumentException($"Unknown T-SQL rule '{ruleName}'."),
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
