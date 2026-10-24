using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class PythonParseTests
{
    [Fact]
    public void Parses_simple_function()
    {
        var lang = new PythonLanguage();
        var code = "def hello(x):\n    return x\n";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 5);
        Assert.Contains(result.Tree.NodesOfKind("file_input"), _ => true);
    }

    [Fact]
    public void Matches_call_pattern()
    {
        var lang = new PythonLanguage();
        var code = "def m():\n    foo(42)\n";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("foo($A)"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits[0].Meta.ContainsKey("A"));
        Assert.Equal("42", hits[0].Meta["A"].Text);
    }
}
