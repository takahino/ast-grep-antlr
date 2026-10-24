using Antlr4.Runtime;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Language;

/// <summary>ANTLR4-backed language: lexer/parser factories plus shared SLL→LL parse helpers.</summary>
public interface IAntlrLanguage : ILanguage
{
    Lexer CreateLexer(ICharStream stream);
    Parser CreateParser(ITokenStream tokens);
    ParserRuleContext InvokeRule(Parser parser, string ruleName);
    bool IsNamedToken(IToken token, IVocabulary vocabulary);
    IVocabulary Vocabulary { get; }

    /// <summary>
    /// Highest token type the vocabulary defines, so callers can enumerate tokens without
    /// guessing an upper bound. IVocabulary does not expose this, but every concrete
    /// vocabulary ANTLR generates is a <see cref="Antlr4.Runtime.Vocabulary"/>.
    /// </summary>
    int MaxTokenType => Vocabulary is Vocabulary v ? v.getMaxTokenType() : 0;

    ParseResult ILanguage.Parse(string source, LanguageParseOptions? options) =>
        AntlrTwoStageParser.Parse(this, source, options);

    SyntaxTree? ILanguage.TryParseFragment(string source, string rule) =>
        AntlrPatternParser.TryParseFragment(this, source, rule);
}
