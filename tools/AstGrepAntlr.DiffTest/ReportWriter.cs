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
        var jsonPath = Path.Combine(reportDir, "report.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

        var summaryPath = Path.Combine(reportDir, "summary.txt");
        await File.WriteAllTextAsync(summaryPath, BuildSummaryText(report), Encoding.UTF8, cancellationToken);
    }

    private static string BuildSummaryText(DiffTestReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ast-grep vs ag4 differential baseline");
        sb.AppendLine($"Generated: {report.GeneratedAtUtc}");
        sb.AppendLine($"ast-grep: @ast-grep/cli@{report.AstGrepVersion}");
        sb.AppendLine($"Mode: {report.Mode}");
        sb.AppendLine();
        sb.AppendLine(
            $"Totals: same={report.Summary.Same} only-ast-grep={report.Summary.OnlyAstGrep} only-ag4={report.Summary.OnlyAg4} range-diff={report.Summary.RangeDiff} metavar-diff={report.Summary.MetaVarDiff}"
        );
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
