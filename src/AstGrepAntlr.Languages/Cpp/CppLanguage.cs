using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Cpp;

namespace AstGrepAntlr.Languages;

public sealed class CppLanguage : ILanguage
{
    public string Id => "cpp";
    public IReadOnlyList<string> Extensions { get; } = [".cpp", ".cc", ".cxx", ".c++", ".hpp", ".hh", ".hxx", ".h"];
    public string StartRule => "translationUnit";
    public IReadOnlyList<string> PatternRules { get; } =
    [
        "expression",
        "primaryExpression",
        "statement",
        "compoundStatement",
        "declaration",
        "functionDefinition",
        "classSpecifier",
        "translationUnit",
    ];
    public string MetaVarPlaceholderTemplate => "__MV{0}__";
    public bool IgnoreCase => false;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => CPP14Parser.ruleNames;
    public IVocabulary Vocabulary => CPP14Lexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new CPP14Lexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new CPP14Parser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (CPP14Parser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "translationunit" or "translationUnit" => p.translationUnit(),
            "expression" => p.expression(),
            "primaryexpression" or "primaryExpression" => p.primaryExpression(),
            "statement" => p.statement(),
            "compoundstatement" or "compoundStatement" => p.compoundStatement(),
            "declaration" => p.declaration(),
            "functiondefinition" or "functionDefinition" => p.functionDefinition(),
            "classspecifier" or "classSpecifier" => p.classSpecifier(),
            _ => throw new ArgumentException($"Unknown C++ rule '{ruleName}'."),
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
