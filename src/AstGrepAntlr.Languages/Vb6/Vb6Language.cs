using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Vb6;

namespace AstGrepAntlr.Languages;

public sealed class Vb6Language : IAntlrLanguage
{
    public string Id => "vb6";
    public IReadOnlyList<string> Extensions { get; } = [".bas", ".cls", ".frm", ".ctl"];
    public string StartRule => "startRule";
    public IReadOnlyList<string> PatternRules { get; } =
    ["blockStmt", "block", "valueStmt", "moduleBodyElement", "ambiguousIdentifier", "literal", "startRule"];

    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => VisualBasic6Parser.ruleNames;
    public IVocabulary Vocabulary => VisualBasic6Lexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new VisualBasic6Lexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new VisualBasic6Parser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (VisualBasic6Parser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "startrule" => p.startRule(),
            "blockstmt" => p.blockStmt(),
            "block" => p.block(),
            "valuestmt" => p.valueStmt(),
            "modulebodyelement" => p.moduleBodyElement(),
            "ambiguousidentifier" => p.ambiguousIdentifier(),
            "literal" => p.literal(),
            _ => throw new ArgumentException($"Unknown VB6 rule '{ruleName}'."),
        };
    }

    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
        if (token.Type < 0)
        {
            return false;
        }

        // The grammar keeps whitespace and line breaks as parser tokens; treat them as punctuation
        // so they never become prefilter literals and Smart matching can skip them.
        if (token.Type is VisualBasic6Lexer.WS or VisualBasic6Lexer.NEWLINE)
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
