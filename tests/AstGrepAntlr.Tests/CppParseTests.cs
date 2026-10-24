using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Search;
using Xunit;

namespace AstGrepAntlr.Tests;

public class CppParseTests
{
    [Fact]
    public void Parses_simple_function()
    {
        var lang = new CppLanguage();
        var code = "void m() { std::cout << 1; }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 5);
        Assert.Contains(result.Tree.NodesOfKind("translationUnit"), _ => true);
    }

    [Fact]
    public void Continues_after_syntax_error()
    {
        var lang = new CppLanguage();
        var code = "void m() { int x = ; std::cout << 1; }";
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
    public void Matches_cout_pattern()
    {
        var lang = new CppLanguage();
        var code = "void m() { std::cout << 42; }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("std::cout << $A"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits[0].Meta.ContainsKey("A"));
        Assert.Equal("42", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Matches_foo_call_in_expression_context()
    {
        var lang = new CppLanguage();
        var code = "void m() { sink(foo(1, 2, 3)); }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("foo($$$ARGS)"));
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("1, 2, 3", hits[0].Meta["ARGS"].Text);
    }

    [Fact]
    public void Matches_identifier_metavar_in_cout()
    {
        var lang = new CppLanguage();
        var code = "void m() { std::cout << x; }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("std::cout << $A"));
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("x", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Registry_resolves_cpp_aliases()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        foreach (var alias in new[] { "cpp", "c++", "cc", "cxx" })
        {
            Assert.IsType<CppLanguage>(registry.Get(alias));
        }
    }
}
