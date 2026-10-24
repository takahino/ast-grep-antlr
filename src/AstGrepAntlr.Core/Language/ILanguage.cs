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

    /// <summary>Placeholder shapes tried when a pattern fails to parse; <c>{0}</c> is the per-slot placeholder text.</summary>
    IReadOnlyList<string> MetaVarVariants => ["{0}"];
    bool IgnoreCase { get; }
    TimeSpan ParseTimeout { get; }
    long MaxFileBytes { get; }

    Lexer CreateLexer(ICharStream stream);
    Parser CreateParser(ITokenStream tokens);
    ParserRuleContext InvokeRule(Parser parser, string ruleName);
    bool IsNamedToken(IToken token, IVocabulary vocabulary);
    IReadOnlyList<string> RuleNames { get; }
    IVocabulary Vocabulary { get; }

    /// <summary>
    /// Highest token type the vocabulary defines, so callers can enumerate tokens without
    /// guessing an upper bound. IVocabulary does not expose this, but every concrete
    /// vocabulary ANTLR generates is a <see cref="Antlr4.Runtime.Vocabulary"/>.
    /// </summary>
    int MaxTokenType => Vocabulary is Vocabulary v ? v.getMaxTokenType() : 0;
}

public sealed class LanguageParseOptions
{
    public bool PreferSll { get; init; } = true;
    public string? EntryRule { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
