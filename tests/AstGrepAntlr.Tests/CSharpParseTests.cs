using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Rules;
using Xunit;

namespace AstGrepAntlr.Tests;

public class CSharpParseTests
{
    [Fact]
    public void Parses_simple_class()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { System.Console.WriteLine(1); } }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 5);
        Assert.NotEmpty(result.Tree.NodesOfKind("compilation_unit"));
        Assert.NotEmpty(result.Tree.NodesOfKind("class_declaration"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Reports_diagnostics_on_syntax_error()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { int x = ; } }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 0);
        Assert.True(result.Diagnostics.Count > 0 || result.Tree.NodesOfKind("ERROR").Count > 0);
    }

    [Fact]
    public void Registry_resolves_csharp_aliases()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        foreach (var alias in new[] { "csharp", "cs", "c#" })
        {
            Assert.IsType<CSharpLanguage>(registry.Get(alias));
        }
    }

    [Fact]
    public void Kind_aliases_match_identifier_and_identifier_name()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { foo(); } }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        Assert.NotEmpty(tree.NodesOfKind("identifier_name"));
        Assert.NotEmpty(tree.NodesOfKind("identifier"));
        var idName = tree.NodesOfKind("identifier_name")[0];
        Assert.True(tree.KindMatches(idName, "identifier"));
        Assert.True(tree.KindMatches(idName, "identifier_name"));
    }

    [Fact]
    public void Utf16_spans_preserve_surrogate_pairs()
    {
        var lang = new CSharpLanguage();
        var src = "class A { string s = \"\U0001F600\"; }";
        var tree = new TwoStageParser().Parse(lang, src).Tree;
        var hit = false;
        for (var i = 0; i < tree.NodeCount; i++)
        {
            var t = tree.Text(i);
            if (!t.Contains("\U0001F600", StringComparison.Ordinal))
            {
                continue;
            }

            hit = true;
            Assert.Equal(src[tree[i].Start..tree[i].End], t);
            if (t is "\"\U0001F600\"")
            {
                Assert.Equal(4, tree[i].End - tree[i].Start);
            }
        }

        Assert.True(hit);
    }

    [Fact]
    public void Public_modifier_is_wrapped_as_modifier_node()
    {
        var lang = new CSharpLanguage();
        var code = "class T { public void M() {} }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        Assert.NotEmpty(tree.NodesOfKind("modifier"));
        var mod = tree.NodesOfKind("modifier")[0];
        Assert.Equal("public", tree.Text(mod));
    }

    [Fact]
    public void Matches_foo_call_with_multi_args()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { Sink(foo(1, 2, 3)); } }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("foo($$$ARGS)"));
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("1, 2, 3", hits[0].Meta["ARGS"].Text);
    }

    [Fact]
    public void Rewrite_replaces_call()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { foo(1); } }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("foo($A)"));
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();
        Assert.Single(hits);
        var (nodeIndex, meta) = hits[0];
        var metaTexts = meta.ToDictionary(kv => kv.Key, kv => kv.Value.Text, StringComparer.Ordinal);
        var replacement = Rewriter.ApplyTemplate("bar($A)", metaTexts, tree.Text(nodeIndex));
        var n = tree[nodeIndex];
        var rewritten = string.Concat(code.AsSpan(0, n.Start), replacement, code.AsSpan(n.End));
        Assert.Contains("bar(1)", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("foo(1)", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Relational_inside_method_declaration()
    {
        var lang = new CSharpLanguage();
        var code = "class T { void M() { foo(1); } int x = foo(2); }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var doc = RuleLoader.LoadRule(
            """
            id: inside-m
            language: csharp
            rule:
              pattern: foo($A)
              inside:
                kind: method_declaration
                stopBy: end
            """
        );
        var hits = new RuleEngine().Match(lang, tree, doc).ToList();
        Assert.Single(hits);
        Assert.Equal("foo(1)", tree.Text(hits[0].NodeIndex));
    }
}
