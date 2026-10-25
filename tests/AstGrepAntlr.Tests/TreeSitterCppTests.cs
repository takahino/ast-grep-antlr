using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class TreeSitterCppTests
{
    private static TreeSitterLanguage? CreateOrSkip()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return null;
        }

        try
        {
            return TreeSitterBuiltins.Cpp();
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
            .Parse(lang, "void f() { int x = 0; x = 1; x += 2; break; return; return x; }")
            .Tree;

        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("$VAR = $VAL;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("$VAR += $VAL;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("break;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("return;"))));
        Assert.NotEmpty(matcher.MatchAll(tree, compiler.Compile(lang, new PatternSpec("return $E;"))));
    }

    [Fact]
    public void Include_patterns_compile_and_match()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        var matcher = new PatternMatcher();
        var tree = new TwoStageParser().Parse(lang, "#include <stdio.h>\n#include \"foo.h\"\n").Tree;

        var angle = compiler.Compile(lang, new PatternSpec("#include <$H>"));
        Assert.NotEmpty(matcher.MatchAll(tree, angle));

        var quote = compiler.Compile(lang, new PatternSpec("#include \"$H\""));
        Assert.NotEmpty(matcher.MatchAll(tree, quote));
    }
}
