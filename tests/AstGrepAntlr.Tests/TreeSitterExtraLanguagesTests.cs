using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

/// <summary>tree-sitter-only built-ins (no ANTLR / Roslyn counterpart).</summary>
public class TreeSitterExtraLanguagesTests
{
    private static ILanguage? GetOrSkip(string id)
    {
        if (!TreeSitterLanguage.NativesAvailable)
        {
            return null;
        }

        try
        {
            return BuiltinLanguages.CreateRegistry().Get(id);
        }
        catch (PlatformNotSupportedException)
        {
            // Grammar native not shipped for this RID (e.g. toml / ocaml on some platforms).
            return null;
        }
    }

    [Theory]
    [InlineData("javascript", "console.log(a);\nconst x = foo(1, 2);\n", "console.log($A)", "a")]
    [InlineData("typescript", "const x: number = foo(1);\n", "const $A: $T = $E", "x")]
    [InlineData("tsx", "const e = <div a={x}>hi</div>;\n", "<div $$$P>$A</div>", "hi")]
    [InlineData("go", "package main\nfunc main() {\n\tx := 1\n}\n", "$A := $V", "x")]
    [InlineData("rust", "fn main() {\n    let x = foo(1);\n}\n", "let $A = $B;", "x")]
    [InlineData("c", "int main(void) {\n    printf(\"%d\", x);\n}\n", "printf($F, $A)", "x")]
    [InlineData("ruby", "def hi(n)\n  puts n\nend\n", "puts $A", "n")]
    [InlineData("php", "<?php\n$x = foo(1);\n", "$A = foo($B);", "$x")]
    [InlineData("bash", "rm -rf /tmp/x\n", "rm -rf $A", "/tmp/x")]
    [InlineData("html", "<div class=\"a\">hi</div>\n", "<div class=\"$A\">$$$</div>", "a")]
    [InlineData("css", ".a { color: red; }\n", "color: $A;", "red")]
    [InlineData("json", "{\"name\": \"x\"}\n", "\"name\": $A", "\"x\"")]
    [InlineData("scala", "object M {\n  def m = {\n    println(x)\n  }\n}\n", "println($A)", "x")]
    [InlineData("haskell", "main = print (foo 1)\n", "foo $A", "1")]
    [InlineData("julia", "function f(x)\n    println(x)\nend\n", "println($A)", "x")]
    [InlineData("ocaml", "let () = print_endline \"hi\"\n", "print_endline $A", "\"hi\"")]
    [InlineData("toml", "[package]\nname = \"x\"\n", "name = $A", "\"x\"")]
    [InlineData("verilog", "module m(input a, output b);\nassign b = a;\nendmodule\n", "assign $L = $A;", "a")]
    public void Pattern_matches_and_captures(string id, string source, string pattern, string expectedA)
    {
        var lang = GetOrSkip(id);
        if (lang is null)
        {
            return;
        }

        var tree = new TwoStageParser().Parse(lang, source).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec(pattern));
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();

        var hit = Assert.Single(hits);
        Assert.True(hit.Meta.TryGetValue("A", out var a), "metavariable $A not captured");
        Assert.Equal(expectedA, a.Text);
    }

    [Theory]
    [InlineData("js", "javascript")]
    [InlineData("jsx", "javascript")]
    [InlineData("ts", "typescript")]
    [InlineData("golang", "go")]
    [InlineData("rs", "rust")]
    [InlineData("rb", "ruby")]
    [InlineData("sh", "bash")]
    [InlineData("hs", "haskell")]
    public void Alias_resolves_to_language(string alias, string id)
    {
        var lang = GetOrSkip(alias);
        if (lang is null)
        {
            return;
        }

        Assert.Equal(id, lang.Id);
    }

    [Fact]
    public void Extra_languages_are_not_backend_switchable()
    {
        Assert.False(BuiltinLanguages.IsBackendAllowed("javascript", LanguageBackend.Antlr));
        Assert.False(BuiltinLanguages.IsBackendAllowed("go", LanguageBackend.TreeSitter));
    }
}
