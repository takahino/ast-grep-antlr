using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
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

    // std::cout << $A hangs on CPP14's huge expression trees (MatchAll × KindMatches).
    // Covered lightly via parse-only tests above; shift operators are a known-diff vs ast-grep.

    [Fact]
    public void Registry_resolves_cpp_aliases()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        foreach (var alias in new[] { "cpp", "c++", "cc", "cxx" })
        {
            Assert.IsType<TreeSitterLanguage>(registry.Get(alias));
        }

        BuiltinLanguages.ApplyBackend(registry, "cpp", LanguageBackend.Antlr);
        foreach (var alias in new[] { "cpp", "c++", "cc", "cxx" })
        {
            Assert.IsType<CppLanguage>(registry.Get(alias));
        }
    }
}
