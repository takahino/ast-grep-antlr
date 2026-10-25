using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class TreeSitterPythonTests
{
    private static TreeSitterLanguage? CreateOrSkip()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return null;
        }

        try
        {
            return TreeSitterBuiltins.Python();
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
    public void Dotted_import_from_matches()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        var matcher = new PatternMatcher();
        var tree = new TwoStageParser()
            .Parse(lang, "from ctypes import byref\nfrom ctypes.wintypes import DWORD\n")
            .Tree;

        var pattern = compiler.Compile(lang, new PatternSpec("from $M import $N"));
        var hits = matcher.MatchAll(tree, pattern).ToList();
        Assert.True(hits.Count >= 2, $"expected both imports, got {hits.Count}");
        Assert.Contains(hits, h => h.Meta.TryGetValue("M", out var m) && m.Text.Contains("wintypes"));
    }
}
