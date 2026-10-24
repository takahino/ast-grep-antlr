using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class PlSqlParseTests
{
    [Fact]
    public void Parses_select_from_dual()
    {
        var lang = new PlSqlLanguage();
        var code = "SELECT 1 FROM dual;\n";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 3);
        Assert.Contains(result.Tree.NodesOfKind("sql_script"), _ => true);
    }

    [Fact]
    public void Matches_select_pattern()
    {
        var lang = new PlSqlLanguage();
        // Leaf token text keeps source casing; match pattern casing to the snippet.
        var code = "SELECT 1 FROM dual;\n";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("SELECT $A FROM dual"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits[0].Meta.ContainsKey("A"));
        Assert.Equal("1", hits[0].Meta["A"].Text);
    }
}
