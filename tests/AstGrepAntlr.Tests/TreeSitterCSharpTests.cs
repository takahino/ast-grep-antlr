using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class TreeSitterCSharpTests
{
    private static TreeSitterLanguage? CreateOrSkip()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return null;
        }

        try
        {
            return TreeSitterBuiltins.CSharp();
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [Fact]
    public void Registry_defaults_csharp_to_tree_sitter()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return;
        }

        var registry = BuiltinLanguages.CreateRegistry();
        Assert.IsType<TreeSitterLanguage>(registry.Get("csharp"));
        Assert.IsType<TreeSitterLanguage>(registry.Get("cs"));
    }

    [Fact]
    public void ApplyBackend_switches_csharp_to_roslyn()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return;
        }

        var registry = BuiltinLanguages.CreateRegistry();
        BuiltinLanguages.ApplyBackend(registry, "csharp", LanguageBackend.Roslyn);
        Assert.IsType<CSharpLanguage>(registry.Get("csharp"));
        Assert.IsType<CSharpLanguage>(registry.Get("c#"));
    }

    [Fact]
    public void Antlr_is_not_allowed_for_csharp()
    {
        Assert.False(BuiltinLanguages.IsBackendAllowed("csharp", LanguageBackend.Antlr));
        Assert.True(BuiltinLanguages.IsBackendAllowed("csharp", LanguageBackend.TreeSitter));
        Assert.True(BuiltinLanguages.IsBackendAllowed("csharp", LanguageBackend.Roslyn));
    }

    [Fact]
    public void Assignment_and_break_patterns_match()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        var matcher = new PatternMatcher();
        var tree = new TwoStageParser()
            .Parse(lang, "class T { void M() { int x = 0; x = 1; x += 2; break; return; return x; } }")
            .Tree;

        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("$VAR = $VAL;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("$VAR += $VAL;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("break;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("return;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("return $E;"))));
    }

    [Fact]
    public void Call_pattern_matches()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var tree = new TwoStageParser().Parse(lang, "class T { void M() { Foo(1, 2); } }").Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("$F($$$ARGS)"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("Foo", hits[0].Meta["F"].Text);
        Assert.NotNull(hits[0].Meta["ARGS"].Parts);
        Assert.Contains(hits[0].Meta["ARGS"].Parts!, p => p.Text == ",");
    }
}
