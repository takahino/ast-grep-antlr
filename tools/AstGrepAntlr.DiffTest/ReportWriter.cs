using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstGrepAntlr.DiffTest;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync(DiffTestReport report, string reportDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(reportDir);
        var jsonPath = Path.Combine(reportDir, "details.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

        // Keep legacy name for Phase 0b consumers.
        await File.WriteAllTextAsync(
            Path.Combine(reportDir, "report.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken
        );

        var summaryPath = Path.Combine(reportDir, "summary.md");
        await File.WriteAllTextAsync(summaryPath, BuildSummaryMarkdown(report), Encoding.UTF8, cancellationToken);

        var summaryTxt = Path.Combine(reportDir, "summary.txt");
        await File.WriteAllTextAsync(summaryTxt, BuildSummaryText(report), Encoding.UTF8, cancellationToken);
    }

    public static string BuildCommittedReport(DiffTestReport report, string? baselineSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Difftest report (ast-grep vs ag4)");
        sb.AppendLine();
        sb.AppendLine($"Generated (UTC): `{report.GeneratedAtUtc}`");
        sb.AppendLine($"Baseline tool: `@ast-grep/cli@{report.AstGrepVersion}`");
        sb.AppendLine($"Mode: `{report.Mode}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(
            $"| Metric | Value |\n| --- | --- |\n| Same | {report.Summary.Same} |\n| Only ast-grep | {report.Summary.OnlyAstGrep} |\n| Only ag4 | {report.Summary.OnlyAg4} |\n| Range diff | {report.Summary.RangeDiff} |\n| Metavar diff | {report.Summary.MetaVarDiff} |\n| Match rate | {report.Summary.MatchRate:P2} |\n| Unexplained diffs | {report.UnexplainedDiffCount} |\n| ast-grep time (ms) | {report.AstGrepTotalMs} |\n| ag4 time (ms) | {report.Ag4TotalMs} |\n| Speed ratio (ag4/ast-grep) | {SpeedRatio(report):F2}x |"
        );
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(baselineSummary))
        {
            sb.AppendLine("## Baseline comparison");
            sb.AppendLine();
            sb.AppendLine("Phase 0b Java baseline (pre-fix) totals for reference:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(baselineSummary.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Per language");
        sb.AppendLine();
        foreach (var langGroup in report.Patterns.GroupBy(p => p.Language).OrderBy(g => g.Key))
        {
            var sum = new DiffSummary();
            long agMs = 0,
                a4Ms = 0;
            foreach (var p in langGroup)
            {
                sum.Add(p.Counts);
                agMs += p.AstGrepMs;
                a4Ms += p.Ag4Ms;
            }

            sb.AppendLine(
                $"### {langGroup.Key}: same={sum.Same} only-ast-grep={sum.OnlyAstGrep} only-ag4={sum.OnlyAg4} range={sum.RangeDiff} metavar={sum.MetaVarDiff} match-rate={sum.MatchRate:P2} time ast-grep={agMs}ms ag4={a4Ms}ms"
            );
            sb.AppendLine();
        }

        sb.AppendLine("## Repositories");
        sb.AppendLine();
        foreach (var repo in report.Repositories)
        {
            sb.AppendLine($"- `{repo.Id}` ({repo.Language}) ref=`{repo.Ref}` sha=`{repo.Sha}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        if (report.Notes.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var note in report.Notes)
            {
                sb.AppendLine($"- {note}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Grammar-caused and documented mismatches live in `difftest/known-differences.yml`.");
        sb.AppendLine("Machine-readable details: `.difftest/report/latest/details.json` (gitignored).");
        return sb.ToString();
    }

    private static double SpeedRatio(DiffTestReport report)
    {
        if (report.AstGrepTotalMs <= 0)
        {
            return 0;
        }

        return (double)report.Ag4TotalMs / report.AstGrepTotalMs;
    }

    private static string BuildSummaryMarkdown(DiffTestReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Difftest summary");
        sb.AppendLine();
        sb.AppendLine($"Generated: {report.GeneratedAtUtc}");
        sb.AppendLine($"ast-grep: @ast-grep/cli@{report.AstGrepVersion}");
        sb.AppendLine(
            $"Totals: same={report.Summary.Same} only-ast-grep={report.Summary.OnlyAstGrep} only-ag4={report.Summary.OnlyAg4} range-diff={report.Summary.RangeDiff} metavar-diff={report.Summary.MetaVarDiff}"
        );
        sb.AppendLine(
            $"Unexplained: {report.UnexplainedDiffCount} (same={report.UnexplainedSummary.Same} only-ast-grep={report.UnexplainedSummary.OnlyAstGrep} only-ag4={report.UnexplainedSummary.OnlyAg4} range={report.UnexplainedSummary.RangeDiff} metavar={report.UnexplainedSummary.MetaVarDiff})"
        );
        sb.AppendLine(
            $"Timing: ast-grep={report.AstGrepTotalMs}ms ag4={report.Ag4TotalMs}ms ratio={SpeedRatio(report):F2}x"
        );
        sb.AppendLine();
        foreach (var pattern in report.Patterns.OrderBy(p => p.Language).ThenBy(p => p.Id).ThenBy(p => p.RepoId))
        {
            sb.AppendLine(
                $"- {pattern.Language}/{pattern.RepoId}/{pattern.Id}: same={pattern.Counts.Same} only-ast-grep={pattern.Counts.OnlyAstGrep} only-ag4={pattern.Counts.OnlyAg4} range={pattern.Counts.RangeDiff} metavar={pattern.Counts.MetaVarDiff} unexplained={pattern.Unexplained.OnlyAstGrep + pattern.Unexplained.OnlyAg4 + pattern.Unexplained.RangeDiff + pattern.Unexplained.MetaVarDiff} t_ag={pattern.AstGrepMs}ms t_ag4={pattern.Ag4Ms}ms"
            );
            if (pattern.AstGrepError is not null)
            {
                sb.AppendLine($"  - ast-grep error: {pattern.AstGrepError}");
            }

            if (pattern.Ag4Error is not null)
            {
                sb.AppendLine($"  - ag4 error: {pattern.Ag4Error}");
            }

            if (pattern.RewriteResult is not null)
            {
                sb.AppendLine($"  - rewrite: {pattern.RewriteResult}");
            }
        }

        return sb.ToString();
    }

    private static string BuildSummaryText(DiffTestReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ast-grep vs ag4 differential");
        sb.AppendLine($"Generated: {report.GeneratedAtUtc}");
        sb.AppendLine($"ast-grep: @ast-grep/cli@{report.AstGrepVersion}");
        sb.AppendLine($"Mode: {report.Mode}");
        sb.AppendLine();
        sb.AppendLine(
            $"Totals: same={report.Summary.Same} only-ast-grep={report.Summary.OnlyAstGrep} only-ag4={report.Summary.OnlyAg4} range-diff={report.Summary.RangeDiff} metavar-diff={report.Summary.MetaVarDiff}"
        );
        sb.AppendLine($"Unexplained diffs: {report.UnexplainedDiffCount}");
        sb.AppendLine($"Timing ms: ast-grep={report.AstGrepTotalMs} ag4={report.Ag4TotalMs}");
        sb.AppendLine();

        foreach (var pattern in report.Patterns.OrderBy(p => p.Id).ThenBy(p => p.RepoId))
        {
            sb.AppendLine(
                $"{pattern.RepoId}/{pattern.Id}: same={pattern.Counts.Same} only-ast-grep={pattern.Counts.OnlyAstGrep} only-ag4={pattern.Counts.OnlyAg4} range-diff={pattern.Counts.RangeDiff} metavar-diff={pattern.Counts.MetaVarDiff}"
            );
            if (pattern.AstGrepError is not null)
            {
                sb.AppendLine($"  ast-grep error: {pattern.AstGrepError}");
            }

            if (pattern.Ag4Error is not null)
            {
                sb.AppendLine($"  ag4 error: {pattern.Ag4Error}");
            }
        }

        return sb.ToString();
    }

    public static NormalizedMatchReport? ToReport(NormalizedMatch? match) =>
        match is null
            ? null
            : new NormalizedMatchReport
            {
                RelativePath = match.RelativePath,
                ByteStart = match.ByteStart,
                ByteEnd = match.ByteEnd,
                Text = match.Text,
                MetaVariables = match.MetaVariables.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
}
