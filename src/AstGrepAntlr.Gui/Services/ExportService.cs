using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstGrepAntlr.Gui.Models;

namespace AstGrepAntlr.Gui.Services;

public enum ExportFormat
{
    Text,
    Json,
    Markdown,
    Html,
    Csv,
}

public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ExportFormat FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => ExportFormat.Json,
            ".md" or ".markdown" => ExportFormat.Markdown,
            ".html" or ".htm" => ExportFormat.Html,
            ".csv" => ExportFormat.Csv,
            _ => ExportFormat.Text,
        };

    public static string Render(ExportFormat format, SearchRequest request, IReadOnlyList<SearchHit> hits) =>
        format switch
        {
            ExportFormat.Json => ToJson(request, hits),
            ExportFormat.Markdown => ToMarkdown(request, hits),
            ExportFormat.Html => ToHtml(request, hits),
            ExportFormat.Csv => ToCsv(hits),
            _ => ToText(request, hits),
        };

    public static void Write(string path, ExportFormat format, SearchRequest request, IReadOnlyList<SearchHit> hits)
    {
        // BOM for CSV so Excel opens UTF-8 correctly; plain UTF-8 elsewhere.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: format == ExportFormat.Csv);
        File.WriteAllText(path, Render(format, request, hits), encoding);
    }

    public static string ToText(SearchRequest request, IReadOnlyList<SearchHit> hits, bool withHeader = true)
    {
        var sb = new StringBuilder();
        if (withHeader)
        {
            foreach (var (key, value) in Conditions(request))
            {
                sb.Append("# ").Append(key).Append(": ").AppendLine(value);
            }

            sb.Append("# hits: ").AppendLine(hits.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        foreach (var hit in hits)
        {
            sb.Append(hit.DisplayPath).Append(':').Append(hit.StartLine).Append(':').Append(hit.StartColumn);
            if (hit.RuleId is not null)
            {
                sb.Append(" [").Append(hit.RuleId).Append(']');
            }

            if (hit.Message is not null)
            {
                sb.Append(' ').Append(hit.Message);
            }

            sb.AppendLine();
            var lineNo = hit.ContextStartLine;
            foreach (var line in hit.ContextText.Split('\n'))
            {
                var marker = lineNo >= hit.StartLine && lineNo <= hit.EndLine ? '>' : ' ';
                sb.Append(marker)
                    .Append(lineNo.ToString(CultureInfo.InvariantCulture).PadLeft(6))
                    .Append(" | ")
                    .AppendLine(line.TrimEnd('\r'));
                lineNo++;
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ToJson(SearchRequest request, IReadOnlyList<SearchHit> hits)
    {
        // Match entries follow `ag4 run --json` naming (0-based line/column) so existing tooling can read them.
        var doc = new
        {
            generatedAt = DateTimeOffset.Now,
            conditions = request,
            total = hits.Count,
            matches = hits.Select(h => new
            {
                text = h.Text,
                file = h.DisplayPath,
                language = h.Language,
                range = new
                {
                    charOffset = new { start = h.Start, end = h.End },
                    start = new { line = h.StartLine - 1, column = h.StartColumn - 1 },
                    end = new { line = h.EndLine - 1, column = h.EndColumn - 1 },
                },
                lines = h.ContextText,
                metaVariables = h.MetaVariables.Count == 0 ? null : h.MetaVariables,
                ruleId = h.RuleId,
                message = h.Message,
                severity = h.Severity,
                encoding = h.Encoding,
            }),
        };
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private static string ToMarkdown(SearchRequest request, IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ag4 search results").AppendLine();
        foreach (var (key, value) in Conditions(request))
        {
            sb.Append("- **").Append(key).Append("**: `").Append(value.Replace("`", "'")).AppendLine("`");
        }

        sb.Append("- **hits**: ").AppendLine(hits.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.AppendLine("| File | Line | Col | Match | Meta | Rule | Message |");
        sb.AppendLine("|---|---:|---:|---|---|---|---|");
        foreach (var h in hits)
        {
            sb.Append("| ")
                .Append(Md(h.DisplayPath))
                .Append(" | ")
                .Append(h.StartLine)
                .Append(" | ")
                .Append(h.StartColumn)
                .Append(" | `")
                .Append(Md(h.TextOneLine).Replace("`", "'"))
                .Append("` | ")
                .Append(Md(h.MetaSummary))
                .Append(" | ")
                .Append(Md(h.RuleId ?? ""))
                .Append(" | ")
                .Append(Md(h.Message ?? ""))
                .AppendLine(" |");
        }

        return sb.ToString();

        static string Md(string s) => s.Replace("|", "\\|");
    }

    private static string ToHtml(SearchRequest request, IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>")
            .AppendLine("<html><head><meta charset=\"utf-8\"><title>ag4 search results</title>")
            .AppendLine("<style>")
            .AppendLine("body{font-family:system-ui,sans-serif;margin:16px}table{border-collapse:collapse;width:100%}")
            .AppendLine("th,td{border:1px solid #ccc;padding:4px 6px;vertical-align:top;text-align:left}")
            .AppendLine("th{background:#f3f3f3}pre{margin:0;font-family:Consolas,monospace;white-space:pre-wrap}")
            .AppendLine("mark{background:#ffe08a}")
            .AppendLine("</style></head><body>")
            .AppendLine("<h1>ag4 search results</h1><ul>");
        foreach (var (key, value) in Conditions(request))
        {
            sb.Append("<li><b>").Append(H(key)).Append("</b>: <code>").Append(H(value)).AppendLine("</code></li>");
        }

        sb.Append("<li><b>hits</b>: ").Append(hits.Count).AppendLine("</li></ul>");
        sb.AppendLine(
            "<table><thead><tr><th>File</th><th>Line</th><th>Col</th><th>Match</th><th>Meta</th><th>Rule</th><th>Message</th></tr></thead><tbody>"
        );
        foreach (var h in hits)
        {
            sb.Append("<tr><td>")
                .Append(H(h.DisplayPath))
                .Append("</td><td>")
                .Append(h.StartLine)
                .Append("</td><td>")
                .Append(h.StartColumn)
                .Append("</td><td><pre><mark>")
                .Append(H(h.Text))
                .Append("</mark></pre></td><td>")
                .Append(H(h.MetaSummary))
                .Append("</td><td>")
                .Append(H(h.RuleId ?? ""))
                .Append("</td><td>")
                .Append(H(h.Message ?? ""))
                .AppendLine("</td></tr>");
        }

        sb.AppendLine("</tbody></table></body></html>");
        return sb.ToString();

        static string H(string s) => WebUtility.HtmlEncode(s);
    }

    private static string ToCsv(IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("file,line,column,end_line,end_column,language,text,meta,rule_id,severity,message");
        foreach (var h in hits)
        {
            string[] cells =
            [
                h.DisplayPath,
                h.StartLine.ToString(CultureInfo.InvariantCulture),
                h.StartColumn.ToString(CultureInfo.InvariantCulture),
                h.EndLine.ToString(CultureInfo.InvariantCulture),
                h.EndColumn.ToString(CultureInfo.InvariantCulture),
                h.Language,
                h.Text,
                h.MetaSummary,
                h.RuleId ?? "",
                h.Severity ?? "",
                h.Message ?? "",
            ];
            sb.AppendJoin(',', cells.Select(Csv)).AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>RFC 4180 quoting, plus a leading <c>'</c> on formula-like cells to block spreadsheet formula injection.</summary>
    private static string Csv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static IEnumerable<(string Key, string Value)> Conditions(SearchRequest r)
    {
        yield return ("mode", r.Mode.ToString());
        yield return ("root", r.Root);
        if (!string.IsNullOrEmpty(r.Pattern))
        {
            yield return ("pattern", r.Pattern.ReplaceLineEndings(" ⏎ "));
        }

        if (r.Mode == SearchMode.Ast)
        {
            yield return ("language", r.Language);
            yield return ("strictness", r.Strictness);
        }

        if (r.Mode == SearchMode.Rule)
        {
            yield return ("ruleSource", r.RuleSource.ToString());
            if (!string.IsNullOrEmpty(r.RulePath))
            {
                yield return ("rulePath", r.RulePath);
            }
        }

        if (!string.IsNullOrEmpty(r.Globs))
        {
            yield return ("globs", r.Globs);
        }

        if (!string.IsNullOrEmpty(r.ExcludeDirs))
        {
            yield return ("excludeDirs", r.ExcludeDirs);
        }

        yield return ("encoding", r.Encoding);
    }
}
