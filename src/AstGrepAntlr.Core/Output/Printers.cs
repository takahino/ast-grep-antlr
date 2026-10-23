using System.Text.Json;
using System.Text.Json.Serialization;
using AstGrepAntlr.Matching;

namespace AstGrepAntlr.Output;

public static class TextPrinter
{
    public static void Print(IEnumerable<MatchResult> matches, TextWriter writer, bool heading = true)
    {
        string? currentFile = null;
        foreach (var m in matches)
        {
            if (heading && currentFile != m.File)
            {
                currentFile = m.File;
                writer.WriteLine(m.File);
            }

            var prefix = $"{m.StartLine}:{m.StartColumn}:";
            if (m.RuleId is not null)
            {
                prefix = $"{m.RuleId}: {prefix}";
            }

            foreach (var line in m.Text.Replace("\r\n", "\n").Split('\n'))
            {
                writer.WriteLine($"{prefix}{line}");
                prefix = new string(' ', prefix.Length);
            }
        }
    }
}

public static class JsonPrinter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static void Print(IEnumerable<MatchResult> matches, TextWriter writer)
    {
        var payload = matches
            .Select(m => new
            {
                file = m.File,
                range = new
                {
                    byteOffset = new { start = m.Start, end = m.End },
                    start = new { line = m.StartLine, column = m.StartColumn },
                    end = new { line = m.EndLine, column = m.EndColumn },
                },
                text = m.Text,
                language = m.Language,
                encoding = m.Encoding,
                ruleId = m.RuleId,
                message = m.Message,
                severity = m.Severity,
                metaVariables = m.MetaVariables.ToDictionary(
                    kv => kv.Key,
                    kv => new { text = kv.Value },
                    StringComparer.Ordinal
                ),
            })
            .ToList();

        writer.WriteLine(JsonSerializer.Serialize(payload, Options));
    }
}
