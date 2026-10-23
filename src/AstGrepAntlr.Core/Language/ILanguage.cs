using Antlr4.Runtime;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Language;

/// <summary>Language front-end: lexer/parser factory and pattern configuration.</summary>
public interface ILanguage
{
    string Id { get; }
    IReadOnlyList<string> Extensions { get; }
    string StartRule { get; }
    IReadOnlyList<string> PatternRules { get; }
    string MetaVarPlaceholderTemplate { get; }
    bool IgnoreCase { get; }
    TimeSpan ParseTimeout { get; }
    long MaxFileBytes { get; }

    Lexer CreateLexer(ICharStream stream);
    Parser CreateParser(ITokenStream tokens);
    ParserRuleContext InvokeRule(Parser parser, string ruleName);
    bool IsNamedToken(IToken token, IVocabulary vocabulary);
    IReadOnlyList<string> RuleNames { get; }
    IVocabulary Vocabulary { get; }
}

public sealed class LanguageParseOptions
{
    public bool PreferSll { get; init; } = true;
    public bool RecoverErrors { get; init; } = true;
    public string? EntryRule { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
