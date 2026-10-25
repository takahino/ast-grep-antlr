using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

public class TreeSitterJavaTests
{
    private static TreeSitterLanguage? CreateOrSkip()
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return null;
        }

        try
        {
            return TreeSitterBuiltins.Java();
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
    public void Parses_with_tree_sitter_kinds()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var result = new TwoStageParser().Parse(lang, "class A { void m() { System.out.println(1); } }");
        Assert.True(result.Tree.NodeCount > 5);
        Assert.Contains(result.Tree.NodesOfKind("program"), _ => true);
        Assert.Contains(result.Tree.NodesOfKind("method_invocation"), _ => true);
    }

    [Fact]
    public void Matches_println_pattern()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var tree = new TwoStageParser().Parse(lang, "class A { void m() { System.out.println(42); } }").Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("System.out.println($A)"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("42", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Utf16_surrogate_offsets()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        // 😀 is one code point / two UTF-16 units.
        var code = "class A { String s = \"😀\"; }";
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        Assert.True(tree.NodeCount > 0);
        Assert.Equal(code.Length, tree[tree.Root].End);
    }

    [Fact]
    public void Parallel_parse_is_safe()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        Parallel.For(
            0,
            32,
            _ =>
            {
                var r = lang.Parse("class A { int x = 1; }");
                Assert.True(r.Tree.NodeCount > 0);
            }
        );
    }

    [Fact]
    public void Backend_switch_registers_antlr()
    {
        var registry = BuiltinLanguages.CreateRegistry(LanguageBackend.Antlr);
        BuiltinLanguages.ApplyBackend(registry, "java", LanguageBackend.Antlr);
        var lang = registry.Get("java");
        Assert.IsAssignableFrom<IAntlrLanguage>(lang);
        var tree = new TwoStageParser().Parse(lang, "class A {}").Tree;
        Assert.Contains(tree.NodesOfKind("compilationunit"), _ => true);
    }

    [Fact]
    public void Compiles_bracey_and_literal_patterns()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        Assert.NotNull(compiler.Compile(lang, new PatternSpec("null")));
        Assert.NotNull(compiler.Compile(lang, new PatternSpec("@Override")));
        Assert.NotNull(compiler.Compile(lang, new PatternSpec("this($$$ARGS)")));
        Assert.NotNull(compiler.Compile(lang, new PatternSpec("if ($C) { $$$B }")));
        Assert.NotNull(compiler.Compile(lang, new PatternSpec("for ($T $V : $ITER) { $$$BODY }")));
        var ret = compiler.Compile(lang, new PatternSpec("return $E;"));
        Assert.True(ret.Tree.KindMatches(ret.Root, "return_statement"));
    }

    [Fact]
    public void Statement_patterns_are_not_miscompiled_as_declarations()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        var matcher = new PatternMatcher();
        var tree = new TwoStageParser()
            .Parse(lang, "class A { void m() { int x = 0; x = 1; x += 2; break; return; return x; } }")
            .Tree;

        var assign = compiler.Compile(lang, new PatternSpec("$VAR = $VAL;"));
        Assert.True(
            assign.Tree.KindMatches(assign.Root, "expression_statement")
                || assign.Tree.KindMatches(assign.Root, "assignment_expression")
        );
        Assert.NotEmpty(matcher.MatchAll(tree, assign));

        var plus = compiler.Compile(lang, new PatternSpec("$VAR += $VAL;"));
        Assert.NotEmpty(matcher.MatchAll(tree, plus));

        var brk = compiler.Compile(lang, new PatternSpec("break;"));
        Assert.True(brk.Tree.KindMatches(brk.Root, "break_statement"));
        Assert.NotEmpty(matcher.MatchAll(tree, brk));

        var retVoid = compiler.Compile(lang, new PatternSpec("return;"));
        Assert.True(retVoid.Tree.KindMatches(retVoid.Root, "return_statement"));
        Assert.NotEmpty(matcher.MatchAll(tree, retVoid));

        var retExpr = compiler.Compile(lang, new PatternSpec("return $E;"));
        Assert.True(retExpr.Tree.KindMatches(retExpr.Root, "return_statement"));
        Assert.NotEmpty(matcher.MatchAll(tree, retExpr));
    }

    [Fact]
    public void Smart_if_block_matches_if_else_but_single_arg_does_not_match_multi()
    {
        var lang = CreateOrSkip();
        if (lang is null)
        {
            return;
        }

        var compiler = new PatternCompiler();
        var matcher = new PatternMatcher();
        var tree = new TwoStageParser()
            .Parse(
                lang,
                """
                class A {
                  void m() {
                    if (a) {
                      // leading
                      foo();
                    } else {
                      bar();
                    }
                    foo(a, b);
                    foo(a);
                  }
                }
                """
            )
            .Tree;

        var ifBlock = compiler.Compile(lang, new PatternSpec("if ($C) { $$$B }"));
        var ifHits = matcher.MatchAll(tree, ifBlock).ToList();
        Assert.NotEmpty(ifHits);
        Assert.Contains(ifHits[0].Meta["B"].Parts ?? [], p => p.Text.Contains("leading", StringComparison.Ordinal));

        var singleArg = compiler.Compile(lang, new PatternSpec("foo($A)"));
        var singleHits = matcher.MatchAll(tree, singleArg).ToList();
        Assert.Contains(singleHits, h => tree.Text(h.NodeIndex) == "foo(a)");
        Assert.DoesNotContain(singleHits, h => tree.Text(h.NodeIndex).Contains("foo(a, b)", StringComparison.Ordinal));

        var multi = compiler.Compile(lang, new PatternSpec("$F($$$ARGS)"));
        var multiHits = matcher.MatchAll(tree, multi).Where(h => h.Meta["F"].Text == "foo").ToList();
        var withTwo = multiHits.First(h => tree.Text(h.NodeIndex).Contains("foo(a, b)", StringComparison.Ordinal));
        Assert.Contains(withTwo.Meta["ARGS"].Parts!, p => p.Text == ",");
        // Concat of parts omits inter-node whitespace (ast-grep JSON multi shape).
        Assert.Equal("a,b", string.Concat(withTwo.Meta["ARGS"].Parts!.Select(p => p.Text)));
    }
}
