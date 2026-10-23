namespace AstGrepAntlr.DiffTest;

internal sealed class DiffTestOptions
{
    public required string RepoRoot { get; init; }

    public required string CorpusPath { get; init; }

    public required string PatternsPath { get; init; }

    public bool SkipClone { get; init; }

    public bool SkipNpm { get; init; }

    public bool NoBuild { get; init; }

    public int SampleLimit { get; init; } = 25;
}

internal static class DiffTestRunner
{
    public static async Task<DiffTestReport> RunAsync(DiffTestOptions options, CancellationToken cancellationToken)
    {
        var corpus = YamlConfigLoader.LoadCorpus(options.CorpusPath);
        var patterns = YamlConfigLoader.LoadPatterns(options.PatternsPath);
        var language = string.IsNullOrWhiteSpace(patterns.Language) ? corpus.Language : patterns.Language;

        var difftestRoot = Path.Combine(options.RepoRoot, ".difftest");
        var toolsDir = Path.Combine(difftestRoot, "tools");
        var corpusRoot = Path.Combine(difftestRoot, "corpus");
        var reportDir = Path.Combine(difftestRoot, "report", "baseline");

        var astGrepExe = await ToolingSetup.EnsureAstGrepAsync(toolsDir, options.SkipNpm, cancellationToken);
        var repos = await CorpusProvisioner.EnsureCorpusAsync(corpus, corpusRoot, options.SkipClone, cancellationToken);

        var patternReports = new List<PatternReport>();
        var total = new DiffSummary();

        foreach (var repo in repos)
        {
            foreach (var rule in patterns.Rules)
            {
                var pattern = rule.Rule.Pattern;
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                var ast = await SearchRunner.RunAstGrepAsync(
                    astGrepExe,
                    pattern,
                    language,
                    repo.Path,
                    cancellationToken
                );
                var ag4 = await SearchRunner.RunAg4Async(
                    options.RepoRoot,
                    pattern,
                    language,
                    repo.Path,
                    options.NoBuild,
                    cancellationToken
                );

                var astMatches = JsonMatchParser.ParseToolOutput(ast.StdOut, repo.Path);
                var ag4Matches = JsonMatchParser.ParseToolOutput(ag4.StdOut, repo.Path);
                var classified = MatchComparer.Classify(astMatches, ag4Matches);

                var counts = new DiffSummary();
                foreach (var item in classified)
                {
                    counts.Add(item);
                }

                total.Add(counts);

                patternReports.Add(
                    new PatternReport
                    {
                        Id = rule.Id,
                        Pattern = pattern,
                        RepoId = repo.Id,
                        AstGrepError = ast.Succeeded ? null : TrimError(ast.StdErr),
                        Ag4Error = ag4.Succeeded ? null : TrimError(ag4.StdErr),
                        Counts = counts,
                        Samples = classified
                            .Where(c => c.Bucket != MatchBucket.Same)
                            .Take(options.SampleLimit)
                            .Select(c => new ClassifiedMatchReport
                            {
                                Bucket = c.Bucket.ToString(),
                                AstGrep = ReportWriter.ToReport(c.AstGrep),
                                Ag4 = ReportWriter.ToReport(c.Ag4),
                            })
                            .ToList(),
                    }
                );
            }
        }

        var report = new DiffTestReport
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Mode = "baseline",
            AstGrepVersion = ToolingSetup.AstGrepVersion,
            Corpus = corpus,
            Repositories = repos
                .Select(r => new PreparedRepoReport
                {
                    Id = r.Id,
                    Path = r.Path,
                    Ref = r.Ref,
                    Sha = r.Sha,
                })
                .ToList(),
            Patterns = patternReports,
            Summary = total,
        };

        await ReportWriter.WriteAsync(report, reportDir, cancellationToken);
        return report;
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
