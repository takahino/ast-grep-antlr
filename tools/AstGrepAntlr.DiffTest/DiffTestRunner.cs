namespace AstGrepAntlr.DiffTest;

internal sealed class DiffTestOptions
{
    public required string RepoRoot { get; init; }

    public required string CorpusPath { get; init; }

    public List<string> PatternsPaths { get; init; } = [];

    public string? KnownDifferencesPath { get; init; }

    public string? SgConfigPath { get; init; }

    public bool SkipClone { get; init; }

    public bool SkipNpm { get; init; }

    public bool SkipBuild { get; init; }

    public bool FailOnUnexplained { get; init; } = true;

    public bool SkipRewrite { get; init; }

    /// <summary>ag4 parser backend for switchable languages (tree-sitter|antlr). Default tree-sitter.</summary>
    public string Ag4Backend { get; init; } = "tree-sitter";

    public int SampleLimit { get; init; } = 25;

    public int RewriteFileLimit { get; init; } = 20;

    public string? LanguageFilter { get; init; }

    public int ToolTimeoutSeconds { get; init; } = 120;
}

internal sealed class DiffTestRunResult
{
    public required DiffTestReport Report { get; init; }

    public required int ExitCode { get; init; }
}

internal static class DiffTestRunner
{
    public static async Task<DiffTestRunResult> RunAsync(DiffTestOptions options, CancellationToken cancellationToken)
    {
        var corpus = YamlConfigLoader.LoadCorpus(options.CorpusPath);
        var knownPath =
            options.KnownDifferencesPath ?? Path.Combine(options.RepoRoot, "difftest", "known-differences.yml");
        var known = YamlConfigLoader.LoadKnownDifferences(knownPath);
        var sgConfig = options.SgConfigPath ?? Path.Combine(options.RepoRoot, "difftest", "sgconfig.yml");
        if (!File.Exists(sgConfig))
        {
            sgConfig = null;
        }

        var patternFiles = ResolvePatternFiles(options);
        var difftestRoot = Path.Combine(options.RepoRoot, ".difftest");
        var toolsDir = Path.Combine(difftestRoot, "tools");
        var corpusRoot = Path.Combine(difftestRoot, "corpus");
        var reportDir = Path.Combine(difftestRoot, "report", "latest");
        var workDir = Path.Combine(difftestRoot, "work");
        Directory.CreateDirectory(workDir);

        var notes = new List<string>();
        SearchRunner.ToolTimeout = TimeSpan.FromSeconds(Math.Max(30, options.ToolTimeoutSeconds));
        string astGrepExe;
        try
        {
            astGrepExe = await ToolingSetup.EnsureAstGrepAsync(toolsDir, options.SkipNpm, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"ast-grep tooling setup failed: {ex.Message}");
            var failed = EmptyReport(corpus, notes);
            await ReportWriter.WriteAsync(failed, reportDir, cancellationToken);
            return new DiffTestRunResult { Report = failed, ExitCode = 2 };
        }

        string ag4Dll;
        try
        {
            ag4Dll = await ToolingSetup.EnsureAg4BuiltAsync(options.RepoRoot, options.SkipBuild, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"ag4 build failed: {ex.Message}");
            var failed = EmptyReport(corpus, notes);
            await ReportWriter.WriteAsync(failed, reportDir, cancellationToken);
            return new DiffTestRunResult { Report = failed, ExitCode = 2 };
        }

        List<PreparedRepo> repos;
        try
        {
            repos = await CorpusProvisioner.EnsureCorpusAsync(corpus, corpusRoot, options.SkipClone, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"corpus clone/provision failed: {ex.Message}");
            var failed = EmptyReport(corpus, notes);
            await ReportWriter.WriteAsync(failed, reportDir, cancellationToken);
            return new DiffTestRunResult { Report = failed, ExitCode = 2 };
        }

        var patternReports = new List<PatternReport>();
        var total = new DiffSummary();
        var unexplainedTotal = new DiffSummary();
        long astMs = 0;
        long ag4Ms = 0;
        var unexplainedCount = 0;

        foreach (var patterns in patternFiles)
        {
            var language = string.IsNullOrWhiteSpace(patterns.Language) ? corpus.Language : patterns.Language;
            if (
                !string.IsNullOrWhiteSpace(options.LanguageFilter)
                && !string.Equals(options.LanguageFilter, language, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            var langRepos = repos
                .Where(r => string.Equals(r.Language, language, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (langRepos.Count == 0)
            {
                notes.Add($"No corpus repos for language '{language}'.");
                continue;
            }

            foreach (var repo in langRepos)
            {
                foreach (var rule in patterns.Rules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Console.WriteLine($"[{language}] {repo.Id}/{rule.Id} ...");
                    PatternReport report;
                    try
                    {
                        report = await RunOnePatternAsync(
                            new OnePatternContext
                            {
                                AstGrepExe = astGrepExe,
                                Ag4Dll = ag4Dll,
                                Language = language,
                                Repo = repo,
                                Rule = rule,
                                SgConfig = language.Equals("cpp", StringComparison.OrdinalIgnoreCase) ? sgConfig : null,
                                WorkDir = workDir,
                                Known = known,
                                SampleLimit = options.SampleLimit,
                                RewriteFileLimit = options.RewriteFileLimit,
                                SkipRewrite = options.SkipRewrite,
                                Ag4Backend = options.Ag4Backend,
                            },
                            cancellationToken
                        );
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"{language}/{repo.Id}/{rule.Id}: {ex.GetType().Name}: {ex.Message}");
                        Console.WriteLine($"  ERROR: {ex.Message}");
                        report = new PatternReport
                        {
                            Id = rule.Id,
                            Pattern = rule.SimplePattern ?? "(composite rule)",
                            RepoId = repo.Id,
                            Language = language,
                            AstGrepError = null,
                            Ag4Error = TrimError(ex.ToString()),
                            Counts = new DiffSummary(),
                            Unexplained = new DiffSummary(),
                            Samples = [],
                        };
                    }

                    patternReports.Add(report);
                    total.Add(report.Counts);
                    unexplainedTotal.Add(report.Unexplained);
                    astMs += report.AstGrepMs;
                    ag4Ms += report.Ag4Ms;
                    unexplainedCount +=
                        report.Unexplained.OnlyAstGrep
                        + report.Unexplained.OnlyAg4
                        + report.Unexplained.RangeDiff
                        + report.Unexplained.MetaVarDiff;
                    Console.WriteLine(
                        $"  same={report.Counts.Same} only-sg={report.Counts.OnlyAstGrep} only-ag4={report.Counts.OnlyAg4} range={report.Counts.RangeDiff} meta={report.Counts.MetaVarDiff} unexplained={report.Unexplained.OnlyAstGrep + report.Unexplained.OnlyAg4 + report.Unexplained.RangeDiff + report.Unexplained.MetaVarDiff} t_sg={report.AstGrepMs}ms t_ag4={report.Ag4Ms}ms"
                    );
                    Console.Out.Flush();
                }
            }
        }

        var reportOut = new DiffTestReport
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Mode = "comparative",
            AstGrepVersion = ToolingSetup.AstGrepVersion,
            Corpus = corpus,
            Repositories = repos
                .Select(r => new PreparedRepoReport
                {
                    Id = r.Id,
                    Path = r.Path,
                    Ref = r.Ref,
                    Sha = r.Sha,
                    Language = r.Language,
                })
                .ToList(),
            Patterns = patternReports,
            Summary = total,
            UnexplainedSummary = unexplainedTotal,
            AstGrepTotalMs = astMs,
            Ag4TotalMs = ag4Ms,
            UnexplainedDiffCount = unexplainedCount,
            Notes = notes,
        };

        await ReportWriter.WriteAsync(reportOut, reportDir, cancellationToken);

        // Do not overwrite .difftest/report/baseline — that directory holds the Phase 0b snapshot.

        var exit =
            options.FailOnUnexplained && unexplainedCount > 0 ? 1
            : notes.Count > 0 && patternReports.Count == 0 ? 2
            : 0;
        return new DiffTestRunResult { Report = reportOut, ExitCode = exit };
    }

    private static List<PatternFile> ResolvePatternFiles(DiffTestOptions options)
    {
        if (options.PatternsPaths.Count > 0)
        {
            return options.PatternsPaths.Select(YamlConfigLoader.LoadPatterns).ToList();
        }

        var dir = Path.Combine(options.RepoRoot, "difftest", "patterns");
        return Directory
            .EnumerateFiles(dir, "*.yml", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(YamlConfigLoader.LoadPatterns)
            .ToList();
    }

    private static DiffTestReport EmptyReport(CorpusConfig corpus, List<string> notes) =>
        new()
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Mode = "comparative",
            AstGrepVersion = ToolingSetup.AstGrepVersion,
            Corpus = corpus,
            Repositories = [],
            Patterns = [],
            Summary = new DiffSummary(),
            UnexplainedSummary = new DiffSummary(),
            Notes = notes,
        };

    private sealed class OnePatternContext
    {
        public required string AstGrepExe { get; init; }
        public required string Ag4Dll { get; init; }
        public required string Language { get; init; }
        public required PreparedRepo Repo { get; init; }
        public required PatternRule Rule { get; init; }
        public string? SgConfig { get; init; }
        public required string WorkDir { get; init; }
        public required KnownDifferencesFile Known { get; init; }
        public int SampleLimit { get; init; }
        public int RewriteFileLimit { get; init; }
        public bool SkipRewrite { get; init; }
        public string Ag4Backend { get; init; } = "tree-sitter";
    }

    private static async Task<PatternReport> RunOnePatternAsync(
        OnePatternContext ctx,
        CancellationToken cancellationToken
    )
    {
        var rule = ctx.Rule;
        var language = ctx.Language;
        var repo = ctx.Repo;
        ToolRunOutcome ast;
        ToolRunOutcome ag4;
        string patternLabel;

        var searchRoots = repo.SearchPaths.Count > 0 ? repo.SearchPaths.ToList() : [repo.Path];
        if (rule.IsSimplePatternRule())
        {
            var pattern = rule.SimplePattern!;
            patternLabel = pattern;
            // Match comparison without applying rewrites.
            ast = await SearchRunner.RunAstGrepAsync(
                ctx.AstGrepExe,
                pattern,
                language,
                searchRoots,
                ctx.SgConfig,
                rewrite: null,
                apply: false,
                cancellationToken
            );
            ag4 = await SearchRunner.RunAg4Async(
                ctx.Ag4Dll,
                pattern,
                language,
                searchRoots,
                ctx.SgConfig,
                rewrite: null,
                apply: false,
                cancellationToken,
                backend: BackendForLanguage(language, ctx.Ag4Backend)
            );
        }
        else
        {
            patternLabel = "(composite rule)";
            var ruleYaml = YamlConfigLoader.WriteRuleDocument(rule.Id, language, rule);
            var rulePath = Path.Combine(ctx.WorkDir, $"{language}-{repo.Id}-{rule.Id}.yml");
            await File.WriteAllTextAsync(rulePath, ruleYaml, cancellationToken);
            ast = await SearchRunner.RunAstGrepScanAsync(
                ctx.AstGrepExe,
                rulePath,
                searchRoots,
                ctx.SgConfig,
                apply: false,
                cancellationToken
            );
            ag4 = await SearchRunner.RunAg4ScanAsync(
                ctx.Ag4Dll,
                rulePath,
                searchRoots,
                ctx.SgConfig,
                apply: false,
                cancellationToken,
                backend: BackendForLanguage(language, ctx.Ag4Backend)
            );
        }

        var astMatches = JsonMatchParser.ParseToolOutput(ast.StdOut, repo.Path);
        var ag4Matches = JsonMatchParser.ParseToolOutput(ag4.StdOut, repo.Path);
        var classified = MatchComparer.Classify(astMatches, ag4Matches);

        var counts = new DiffSummary();
        var unexplained = new DiffSummary();
        var samples = new List<ClassifiedMatchReport>();
        foreach (var item in classified)
        {
            counts.Add(item);
            var explained = KnownDifferenceMatcher.IsExplained(
                ctx.Known,
                language,
                rule.Id,
                repo.Id,
                item.Bucket,
                BackendForLanguage(language, ctx.Ag4Backend)
            );
            if (!explained && item.Bucket != MatchBucket.Same)
            {
                unexplained.Add(item);
            }

            if (item.Bucket != MatchBucket.Same && samples.Count < ctx.SampleLimit)
            {
                samples.Add(
                    new ClassifiedMatchReport
                    {
                        Bucket = item.Bucket.ToString(),
                        Explained = explained,
                        AstGrep = ReportWriter.ToReport(item.AstGrep),
                        Ag4 = ReportWriter.ToReport(item.Ag4),
                    }
                );
            }
        }

        string? rewriteResult = null;
        if (!ctx.SkipRewrite && rule.HasRewrite && rule.IsSimplePatternRule())
        {
            rewriteResult = await CompareRewriteAsync(
                ctx,
                rule.SimplePattern!,
                rule.EffectiveRewrite,
                cancellationToken
            );
        }

        return new PatternReport
        {
            Id = rule.Id,
            Pattern = patternLabel,
            RepoId = repo.Id,
            Language = language,
            AstGrepError = ast.Succeeded ? null : TrimError(ast.StdErr),
            Ag4Error = ag4.Succeeded ? null : TrimError(ag4.StdErr),
            AstGrepExitCode = ast.ExitCode,
            Ag4ExitCode = ag4.ExitCode,
            AstGrepMs = ast.ElapsedMs,
            Ag4Ms = ag4.ElapsedMs,
            Counts = counts,
            Unexplained = unexplained,
            Samples = samples,
            RewriteResult = rewriteResult,
        };
    }

    private static async Task<string> CompareRewriteAsync(
        OnePatternContext ctx,
        string pattern,
        string rewrite,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var root = Path.Combine(ctx.WorkDir, "rewrite", ctx.Language, ctx.Repo.Id, ctx.Rule.Id);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            var left = Path.Combine(root, "ast-grep");
            var right = Path.Combine(root, "ag4");
            Directory.CreateDirectory(left);
            Directory.CreateDirectory(right);

            // Probe matches first to limit rewrite surface.
            var searchRoots = ctx.Repo.SearchPaths.Count > 0 ? ctx.Repo.SearchPaths.ToList() : [ctx.Repo.Path];
            var probe = await SearchRunner.RunAg4Async(
                ctx.Ag4Dll,
                pattern,
                ctx.Language,
                searchRoots,
                ctx.SgConfig,
                rewrite: null,
                apply: false,
                cancellationToken,
                backend: BackendForLanguage(ctx.Language, ctx.Ag4Backend)
            );
            var matches = JsonMatchParser.ParseToolOutput(probe.StdOut, ctx.Repo.Path);
            var files = matches
                .Select(m => m.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(ctx.RewriteFileLimit)
                .ToList();
            if (files.Count == 0)
            {
                return "skipped (no matches)";
            }

            foreach (var rel in files)
            {
                var src = Path.Combine(ctx.Repo.Path, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src))
                {
                    continue;
                }

                var dstL = Path.Combine(left, rel.Replace('/', Path.DirectorySeparatorChar));
                var dstR = Path.Combine(right, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dstL)!);
                Directory.CreateDirectory(Path.GetDirectoryName(dstR)!);
                File.Copy(src, dstL, overwrite: true);
                File.Copy(src, dstR, overwrite: true);
            }

            await SearchRunner.RunAstGrepAsync(
                ctx.AstGrepExe,
                pattern,
                ctx.Language,
                [left],
                ctx.SgConfig,
                rewrite,
                apply: true,
                cancellationToken
            );
            await SearchRunner.RunAg4Async(
                ctx.Ag4Dll,
                pattern,
                ctx.Language,
                [right],
                ctx.SgConfig,
                rewrite,
                apply: true,
                cancellationToken,
                backend: BackendForLanguage(ctx.Language, ctx.Ag4Backend)
            );

            var diffs = 0;
            var compared = 0;
            foreach (var rel in files)
            {
                var a = Path.Combine(left, rel.Replace('/', Path.DirectorySeparatorChar));
                var b = Path.Combine(right, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(a) || !File.Exists(b))
                {
                    continue;
                }

                compared++;
                var ba = await File.ReadAllBytesAsync(a, cancellationToken);
                var bb = await File.ReadAllBytesAsync(b, cancellationToken);
                if (!ba.AsSpan().SequenceEqual(bb))
                {
                    diffs++;
                }
            }

            return $"files={compared} byte-diffs={diffs}";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    /// <summary>Only java/cpp/python accept --backend; csharp/plsql ignore it.</summary>
    private static string? BackendForLanguage(string language, string? backend)
    {
        if (string.IsNullOrWhiteSpace(backend))
        {
            return null;
        }

        return language.ToLowerInvariant() switch
        {
            "java" or "cpp" or "python" or "c++" or "py" => backend,
            _ => null,
        };
    }

    private static string? TrimError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var line = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return line is { Length: > 500 } ? line[..500] : line;
    }
}
