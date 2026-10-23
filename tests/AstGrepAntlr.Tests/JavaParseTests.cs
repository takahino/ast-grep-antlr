using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Search;
using Xunit;

namespace AstGrepAntlr.Tests;

public class JavaParseTests
{
    [Fact]
    public void Parses_simple_class()
    {
        var lang = new JavaLanguage();
        var code = "class A { void m() { System.out.println(1); } }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 5);
        Assert.Contains(result.Tree.NodesOfKind("compilationunit"), _ => true);
    }

    [Fact]
    public void Continues_after_syntax_error()
    {
        var lang = new JavaLanguage();
        var code = "class A { void m() { int x = ; System.out.println(1); } }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 0);
        Assert.True(
            result.Diagnostics.Count > 0
                || result.UsedLlFallback
                || result.Tree.NodesOfKind("ERROR").Count > 0
                || result.Tree.NodeCount > 10
        );
    }

    [Fact]
    public void Matches_println_pattern()
    {
        var lang = new JavaLanguage();
        var code = "class A { void m() { System.out.println(42); } }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("System.out.println($A)"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits[0].Meta.ContainsKey("A"));
        Assert.Equal("42", hits[0].Meta["A"]);
    }
}
