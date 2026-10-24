using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Sqlite;

namespace AstGrepAntlr.Languages;

public sealed class SqliteLanguage : IAntlrLanguage
{
    public string Id => "sqlite";
    public IReadOnlyList<string> Extensions { get; } = [".sql", ".sqlite"];
    public string StartRule => "parse";
    public IReadOnlyList<string> PatternRules { get; } =
    ["select_stmt", "sql_stmt", "expr", "table_or_subquery", "parse"];
    public string MetaVarPlaceholderTemplate => "__MV{0}__";
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0};"];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;
    public IReadOnlyList<string> RuleNames => SQLiteParser.ruleNames;
    public IVocabulary Vocabulary => SQLiteLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new SQLiteLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new SQLiteParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (SQLiteParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "parse" => p.parse(),
            "sql_stmt" => p.sql_stmt(),
            "select_stmt" => p.select_stmt(),
            "expr" => p.expr(),
            "table_or_subquery" => p.table_or_subquery(),
            _ => throw new ArgumentException($"Unknown SQLite rule '{ruleName}'."),
        };
    }

    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
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
