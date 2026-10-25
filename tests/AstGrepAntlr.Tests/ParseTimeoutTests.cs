using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

public class ParseTimeoutTests
{
    [Fact]
    public void Antlr_java_throws_when_cancellation_requested()
    {
        var lang = new JavaLanguage();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILanguage ilang = lang;
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ilang.Parse(
                "class A { void m() { int x = 1; } }",
                new LanguageParseOptions { CancellationToken = cts.Token }
            )
        );
    }

    [Fact]
    public void Interpreted_language_throws_when_cancellation_requested()
    {
        var lang = InterpretedLanguage.FromInterpFiles(
            "calc",
            [".calc"],
            "program",
            ["statement", "expr", "program"],
            "__MV{0}__",
            ignoreCase: false,
            Fixtures.CalcLexerInterp,
            Fixtures.CalcParserInterp
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILanguage ilang = lang;
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ilang.Parse("x = 1;", new LanguageParseOptions { CancellationToken = cts.Token })
        );
    }
}
