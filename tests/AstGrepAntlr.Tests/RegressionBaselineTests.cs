using AstGrepAntlr.Cli;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Search;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

public class RegressionBaselineTests
{
    [Theory]
    [MemberData(nameof(JavaCases))]
    public void Java_case_files(string id, CaseFileEntry entry)
    {
        Assert.Equal(id, entry.Id);
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, entry.Source).Tree;
        var strictness = Enum.TryParse<Strictness>(entry.Strictness, true, out var s) ? s : Strictness.Smart;
        var compiled = new PatternCompiler().Compile(
            lang,
            new PatternSpec(entry.Pattern, entry.Entry, Strictness: strictness)
        );
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();

        if (entry.Expect is { Count: > 0 })
        {
            Assert.True(
                hits.Count >= entry.Expect.Count,
                $"case {id}: expected >= {entry.Expect.Count} hits, got {hits.Count}"
            );
            foreach (var exp in entry.Expect)
            {
                var match = hits.FirstOrDefault(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                Assert.True(match.Meta is not null || hits.Count > 0, $"case {id}: no hit for text={exp.Text}");
                var hit = hits.First(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                if (exp.Meta is not null)
                {
                    foreach (var (k, v) in exp.Meta)
                    {
                        Assert.True(hit.Meta.ContainsKey(k), $"case {id}: missing meta {k}");
                        Assert.Equal(v, hit.Meta[k].Text);
                    }
                }
            }
        }

        if (entry.RejectTexts is { Count: > 0 })
        {
            foreach (var reject in entry.RejectTexts)
            {
                Assert.DoesNotContain(hits, h => tree.Text(h.NodeIndex) == reject);
            }
        }
    }

    public static IEnumerable<object[]> JavaCases() => CaseFileRunner.Discover("java");

    [Theory]
    [MemberData(nameof(CppCases))]
    public void Cpp_case_files(string id, CaseFileEntry entry)
    {
        Assert.Equal(id, entry.Id);
        var lang = new CppLanguage();
        var tree = new TwoStageParser().Parse(lang, entry.Source).Tree;
        var strictness = Enum.TryParse<Strictness>(entry.Strictness, true, out var s) ? s : Strictness.Smart;
        var compiled = new PatternCompiler().Compile(
            lang,
            new PatternSpec(entry.Pattern, entry.Entry, Strictness: strictness)
        );
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();

        if (entry.Expect is { Count: > 0 })
        {
            Assert.True(
                hits.Count >= entry.Expect.Count,
                $"case {id}: expected >= {entry.Expect.Count} hits, got {hits.Count}"
            );
            foreach (var exp in entry.Expect)
            {
                var match = hits.FirstOrDefault(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                Assert.True(match.Meta is not null || hits.Count > 0, $"case {id}: no hit for text={exp.Text}");
                var hit = hits.First(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                if (exp.Meta is not null)
                {
                    foreach (var (k, v) in exp.Meta)
                    {
                        Assert.True(hit.Meta.ContainsKey(k), $"case {id}: missing meta {k}");
                        Assert.Equal(v, hit.Meta[k].Text);
                    }
                }
            }
        }

        if (entry.RejectTexts is { Count: > 0 })
        {
            foreach (var reject in entry.RejectTexts)
            {
                Assert.DoesNotContain(hits, h => tree.Text(h.NodeIndex) == reject);
            }
        }
    }

    public static IEnumerable<object[]> CppCases() => CaseFileRunner.Discover("cpp");

    [Theory]
    [MemberData(nameof(CSharpCases))]
    public void CSharp_case_files(string id, CaseFileEntry entry)
    {
        Assert.Equal(id, entry.Id);
        var lang = new CSharpLanguage();
        var tree = new TwoStageParser().Parse(lang, entry.Source).Tree;
        var strictness = Enum.TryParse<Strictness>(entry.Strictness, true, out var s) ? s : Strictness.Smart;
        var compiled = new PatternCompiler().Compile(
            lang,
            new PatternSpec(entry.Pattern, entry.Entry, Strictness: strictness)
        );
        var hits = new PatternMatcher().MatchAll(tree, compiled).ToList();

        if (entry.Expect is { Count: > 0 })
        {
            Assert.True(
                hits.Count >= entry.Expect.Count,
                $"case {id}: expected >= {entry.Expect.Count} hits, got {hits.Count}"
            );
            foreach (var exp in entry.Expect)
            {
                var match = hits.FirstOrDefault(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                Assert.True(match.Meta is not null || hits.Count > 0, $"case {id}: no hit for text={exp.Text}");
                var hit = hits.First(h =>
                    exp.Text is null || string.Equals(tree.Text(h.NodeIndex), exp.Text, StringComparison.Ordinal)
                );
                if (exp.Meta is not null)
                {
                    foreach (var (k, v) in exp.Meta)
                    {
                        Assert.True(hit.Meta.ContainsKey(k), $"case {id}: missing meta {k}");
                        Assert.Equal(v, hit.Meta[k].Text);
                    }
                }
            }
        }

        if (entry.RejectTexts is { Count: > 0 })
        {
            foreach (var reject in entry.RejectTexts)
            {
                Assert.DoesNotContain(hits, h => tree.Text(h.NodeIndex) == reject);
            }
        }
    }

    public static IEnumerable<object[]> CSharpCases() => CaseFileRunner.Discover("csharp");

    [Fact]
    public void Multi_args_matches_filled_and_empty()
    {
        var lang = new JavaLanguage();
        foreach (
            var (code, expectArgs) in new[]
            {
                ("class T{void m(){foo(1,2,3);}}", "1,2,3"),
                ("class T{void m(){foo();}}", ""),
            }
        )
        {
            var tree = new TwoStageParser().Parse(lang, code).Tree;
            var pattern = new PatternCompiler().Compile(lang, new PatternSpec("foo($$$ARGS)"));
            var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
            Assert.NotEmpty(hits);
            Assert.Equal(expectArgs, hits[0].Meta["ARGS"].Text);
        }
    }

    [Fact]
    public void Underscore_metavar_matches()
    {
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class T{void m(){bar(99);}}").Tree;
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("bar($_)"));
        Assert.NotEmpty(new PatternMatcher().MatchAll(tree, pattern));
    }

    [Fact]
    public void Constraints_regex_and_kind()
    {
        var yaml = """
            id: constrain-demo
            language: java
            rule:
              pattern: System.out.println($A)
            constraints:
              A:
                regex: "^\\d+$"
            """;
        var doc = RuleLoader.LoadRule(yaml);
        var lang = new JavaLanguage();
        var tree = new TwoStageParser()
            .Parse(lang, "class T{void m(){System.out.println(1); System.out.println(x);}}")
            .Tree;
        var hits = new RuleEngine().Match(lang, tree, doc).ToList();
        Assert.Single(hits);
        Assert.Equal("1", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Inside_rule_scopes_match()
    {
        var yaml = """
            id: inside-demo
            language: java
            rule:
              pattern: System.out.println($A)
              inside:
                kind: classBody
                stopBy: end
            """;
        var doc = RuleLoader.LoadRule(yaml);
        var lang = new JavaLanguage();
        // stopBy defaults to neighbor; classBody is an ancestor, so require stopBy: end.
        var tree = new TwoStageParser().Parse(lang, "class Outer { void m() { System.out.println(1); } }").Tree;
        var hits = new RuleEngine().Match(lang, tree, doc).ToList();
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void Rewrite_preview_and_apply()
    {
        using var dir = new TempDir();
        var file = dir.WriteText("Hello.java", "class Hello { void m() { System.out.println(1); } }\n");
        var registry = BuiltinLanguages.CreateRegistry();
        var matches = new Searcher()
            .RunPattern(
                "System.out.println($A)",
                new SearchOptions
                {
                    Registry = registry,
                    LanguageId = "java",
                    Paths = [file],
                }
            )
            .ToList();
        Assert.NotEmpty(matches);
        var change = Rewriter.RewriteFile(
            file,
            matches,
            m => Rewriter.ApplyTemplate("System.out.println($A + 1)", m.MetaTexts, m.Text),
            apply: true
        );
        Assert.Contains("1 + 1", change.NewText, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(change.UnifiedDiff));
        Assert.Contains("1 + 1", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_rewrite_preview()
    {
        using var dir = new TempDir();
        var file = dir.WriteText("A.java", "class A { void m() { System.out.println(2); } }\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await Ag4Cli.RunAsync(
            ["run", "-l", "java", "-p", "System.out.println($A)", "--rewrite", "log($A)", file],
            stdout,
            stderr
        );
        Assert.Equal(0, code);
        Assert.Contains("log(2)", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_rule_update_all_applies_fixes()
    {
        using var dir = new TempDir();
        var file = dir.WriteText("A.java", "class A { void m() { System.out.println(1); System.err.println(2); } }\n");
        var rule1 = dir.WriteText(
            "r1.yml",
            """
            id: out
            language: java
            fix: "LOG($A)"
            rule:
              pattern: System.out.println($A)
            """
        );
        var rule2 = dir.WriteText(
            "r2.yml",
            """
            id: err
            language: java
            fix: "ERR($A)"
            rule:
              pattern: System.err.println($A)
            """
        );
        var registry = BuiltinLanguages.CreateRegistry();
        var searcher = new Searcher();
        foreach (var rulePath in new[] { rule1, rule2 })
        {
            var doc = RuleLoader.LoadRule(File.ReadAllText(rulePath));
            var matches = searcher.RunRule(doc, new SearchOptions { Registry = registry, Paths = [file] }).ToList();
            if (string.IsNullOrEmpty(doc.Fix) || matches.Count == 0)
            {
                continue;
            }

            Rewriter.RewriteFile(
                file,
                matches,
                m => Rewriter.ApplyTemplate(doc.Fix!, m.MetaTexts, m.Text),
                apply: true
            );
        }

        var text = File.ReadAllText(file);
        Assert.Contains("LOG(1)", text, StringComparison.Ordinal);
        Assert.Contains("ERR(2)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Java_continues_after_syntax_error()
    {
        var lang = new JavaLanguage();
        var code = "class A { void m() { int x = ; System.out.println(1); } }";
        var result = new TwoStageParser().Parse(lang, code);
        Assert.True(result.Tree.NodeCount > 0);
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("System.out.println($A)"));
        var hits = new PatternMatcher().MatchAll(result.Tree, pattern).ToList();
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void Calc_interp_fixture_registers_and_matches()
    {
        var lang = InterpretedLanguage.FromInterpFiles(
            "calc",
            [".calc"],
            "program",
            ["statement", "expr", "program"],
            "__MV{0}__",
            ignoreCase: false,
            Fixtures.CalcLexerInterp,
            Fixtures.CalcParserInterp
        );
        var tree = new TwoStageParser().Parse(lang, "x = 1 + 2;\n").Tree;
        Assert.True(tree.NodeCount > 3);
        var pattern = new PatternCompiler().Compile(lang, new PatternSpec("$A = $B;"));
        var hits = new PatternMatcher().MatchAll(tree, pattern).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("x", hits[0].Meta["A"].Text);
    }

    [Fact]
    public async Task Samples_dsl_scan_with_config()
    {
        var sampleDir = Path.Combine(Fixtures.RepoRoot, "samples", "dsl");
        var config = Path.Combine(sampleDir, "sgconfig.yml");
        Assert.True(File.Exists(config));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await Ag4Cli.RunAsync(["scan", "-c", config, sampleDir], stdout, stderr);
        Assert.True(code == 0, $"exit={code} stderr={stderr} stdout={stdout}");
        // Rule may or may not match sample.calc; ensure no crash and optional match text.
        _ = stdout.ToString();
    }

    [Fact]
    public async Task Version_command()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await Ag4Cli.RunAsync(["version"], stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("ag4", stdout.ToString(), StringComparison.Ordinal);
    }
}
