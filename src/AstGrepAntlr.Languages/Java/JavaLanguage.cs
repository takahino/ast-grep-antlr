using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Java;

namespace AstGrepAntlr.Languages;

public sealed class JavaLanguage : ILanguage
{
    public string Id => "java";
    public IReadOnlyList<string> Extensions { get; } = [".java"];
    public string StartRule => "compilationunit";
    public IReadOnlyList<string> PatternRules { get; } =
    [
        "expression",
        "primary",
        "statement",
        "blockStatement",
        "classBodyDeclaration",
        "memberDeclaration",
        "methodDeclaration",
        "classDeclaration",
        "typeDeclaration",
        "typeType",
        "qualifiedName",
        "compilationunit",
    ];
    public string MetaVarPlaceholderTemplate => "__MV{0}__";

    // Variants tried per metavar (odometer). Extra forms cover class-body members.
    public IReadOnlyList<string> MetaVarVariants { get; } =
    ["{0}", "{0};", "{0} {0}", "{0} {0};", "int {0};", "void {0}(){}", ";"];
    public bool IgnoreCase => false;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => JavaParser.ruleNames;
    public IVocabulary Vocabulary => JavaLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new JavaLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new JavaParser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (JavaParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "compilationunit" or "compilationUnit" => p.compilationunit(),
            "typedeclaration" or "typeDeclaration" => p.typeDeclaration(),
            "classbodydeclaration" or "classBodyDeclaration" => p.classBodyDeclaration(),
            "memberdeclaration" or "memberDeclaration" => p.memberDeclaration(),
            "methoddeclaration" or "methodDeclaration" => p.methodDeclaration(),
            "classdeclaration" or "classDeclaration" => p.classDeclaration(),
            "blockstatement" or "blockStatement" => p.blockStatement(),
            "statement" => p.statement(),
            "expression" => p.expression(),
            "primary" => p.primary(),
            "typetype" or "typeType" => p.typeType(),
            "qualifiedname" or "qualifiedName" => p.qualifiedName(),
            _ => throw new ArgumentException($"Unknown Java rule '{ruleName}'."),
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
        // Literal-only tokens like '(' are unnamed; symbolic Identifier etc. are named.
        if (
            !string.IsNullOrEmpty(literal)
            && literal.StartsWith('\'')
            && string.Equals(symbolic, literal.Trim('\''), StringComparison.OrdinalIgnoreCase)
        )
        {
            // keyword with both names — treat as unnamed punctuation if single char, else named keyword.
            var inner = literal.Trim('\'');
            return inner.Length > 1 && char.IsLetter(inner[0]);
        }

        if (!string.IsNullOrEmpty(literal) && literal.StartsWith('\'') && literal.Length <= 4)
        {
            // punctuation
            var inner = literal.Trim('\'');
            if (inner.Length <= 2 && !inner.All(char.IsLetter))
            {
                return false;
            }
        }

        return !string.IsNullOrEmpty(symbolic);
    }
}
