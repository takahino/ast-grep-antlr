using System.CommandLine;

namespace AstGrepAntlr.DiffTest;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootOption = new Option<string?>("--repo-root")
        {
            Description = "Repository root (defaults to parent of AstGrepAntlr.slnx)",
        };
        var corpusOption = new Option<string?>("--corpus") { Description = "Path to corpus.yml" };
        var patternsOption = new Option<string[]>("--patterns")
        {
            Description = "Pattern YAML file(s); default: all difftest/patterns/*.yml",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
        };
        var knownOption = new Option<string?>("--known-differences") { Description = "Path to known-differences.yml" };
        var configOption = new Option<string?>("--sgconfig") { Description = "sgconfig.yml for languageGlobs" };
        var skipCloneOption = new Option<bool>("--skip-clone")
        {
            Description = "Reuse existing .difftest/corpus clones",
        };
        var skipNpmOption = new Option<bool>("--skip-npm") { Description = "Skip npm install for ast-grep CLI" };
        var skipBuildOption = new Option<bool>("--no-build")
        {
            Description = "Reuse existing Release ag4.dll without rebuilding",
        };
        var languageOption = new Option<string?>("--language")
        {
            Description = "Only run patterns for this language (java|cpp|csharp|python)",
        };
        var noFailOption = new Option<bool>("--no-fail")
        {
            Description = "Always exit 0 even when unexplained diffs remain",
        };
        var skipRewriteOption = new Option<bool>("--skip-rewrite")
        {
            Description = "Skip rewrite apply byte-comparison (match compare still runs)",
        };
        var timeoutOption = new Option<int>("--timeout-seconds")
        {
            Description = "Per-tool-invocation timeout",
            DefaultValueFactory = _ => 120,
        };
        var writeReportOption = new Option<bool>("--write-report")
        {
            Description = "Write difftest/REPORT.md from the run",
            DefaultValueFactory = _ => true,
        };
        var backendOption = new Option<string>("--backend")
        {
            Description = "ag4 parser backend for java/cpp/python: tree-sitter (default) or antlr",
            DefaultValueFactory = _ => "tree-sitter",
        };

        var rootCommand = new RootCommand("ast-grep vs ag4 differential test harness");
        rootCommand.Options.Add(rootOption);
        rootCommand.Options.Add(corpusOption);
        rootCommand.Options.Add(patternsOption);
        rootCommand.Options.Add(knownOption);
        rootCommand.Options.Add(configOption);
        rootCommand.Options.Add(skipCloneOption);
        rootCommand.Options.Add(skipNpmOption);
        rootCommand.Options.Add(skipBuildOption);
        rootCommand.Options.Add(languageOption);
        rootCommand.Options.Add(noFailOption);
        rootCommand.Options.Add(skipRewriteOption);
        rootCommand.Options.Add(timeoutOption);
        rootCommand.Options.Add(writeReportOption);
        rootCommand.Options.Add(backendOption);

        var exitCode = 0;
        rootCommand.SetAction(async parseResult =>
        {
            try
            {
                var repoRoot = parseResult.GetValue(rootOption) ?? RepoPaths.FindRepositoryRoot();
                var corpusPath = parseResult.GetValue(corpusOption) ?? Path.Combine(repoRoot, "difftest", "corpus.yml");
                var patterns = parseResult.GetValue(patternsOption) ?? [];

                var result = await DiffTestRunner.RunAsync(
                    new DiffTestOptions
                    {
                        RepoRoot = repoRoot,
                        CorpusPath = corpusPath,
                        PatternsPaths = patterns.ToList(),
                        KnownDifferencesPath = parseResult.GetValue(knownOption),
                        SgConfigPath = parseResult.GetValue(configOption),
                        SkipClone = parseResult.GetValue(skipCloneOption),
                        SkipNpm = parseResult.GetValue(skipNpmOption),
                        SkipBuild = parseResult.GetValue(skipBuildOption),
                        FailOnUnexplained = !parseResult.GetValue(noFailOption),
                        SkipRewrite = parseResult.GetValue(skipRewriteOption),
                        ToolTimeoutSeconds = parseResult.GetValue(timeoutOption),
                        LanguageFilter = parseResult.GetValue(languageOption),
                        Ag4Backend = parseResult.GetValue(backendOption) ?? "tree-sitter",
                    },
                    CancellationToken.None
                );

                var report = result.Report;
                var reportDir = Path.Combine(repoRoot, ".difftest", "report", "latest");
                Console.WriteLine(
                    $"Report written to {reportDir} (same={report.Summary.Same}, only-ast-grep={report.Summary.OnlyAstGrep}, only-ag4={report.Summary.OnlyAg4}, range-diff={report.Summary.RangeDiff}, metavar-diff={report.Summary.MetaVarDiff}, unexplained={report.UnexplainedDiffCount}, ast-grep={report.AstGrepTotalMs}ms, ag4={report.Ag4TotalMs}ms)."
                );

                if (parseResult.GetValue(writeReportOption))
                {
                    var baselineSummary = TryReadBaselineTotals(repoRoot);
                    var committed = ReportWriter.BuildCommittedReport(report, baselineSummary);
                    var outPath = Path.Combine(repoRoot, "difftest", "REPORT.md");
                    await File.WriteAllTextAsync(outPath, committed);
                    Console.WriteLine($"Committed summary written to {outPath}");
                }

                exitCode = result.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"difftest failed: {ex}");
                exitCode = 2;
            }
        });

        var parseExit = await rootCommand.Parse(args).InvokeAsync();
        if (parseExit != 0)
        {
            return parseExit;
        }

        return exitCode;
    }

    private static string? TryReadBaselineTotals(string repoRoot)
    {
        var pinned = Path.Combine(repoRoot, "difftest", "baseline-phase0b.txt");
        if (File.Exists(pinned))
        {
            return File.ReadAllText(pinned).Trim();
        }

        return "Totals: same=49172 only-ast-grep=14122 only-ag4=649 range-diff=3541 metavar-diff=17048 (Phase 0b Java snapshot)";
    }
}
