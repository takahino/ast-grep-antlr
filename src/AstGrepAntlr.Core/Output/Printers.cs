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

public enum JsonOutputFormat
{
    Pretty,
    Stream,
    Compact,
}

public static class JsonPrinter
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static void Print(
        IEnumerable<MatchResult> matches,
        TextWriter writer,
        JsonOutputFormat format = JsonOutputFormat.Pretty
    )
    {
        var cwd = Directory.GetCurrentDirectory();
        var payload = matches.Select(m => ToObject(m, cwd)).ToList();

        switch (format)
        {
            case JsonOutputFormat.Stream:
                foreach (var item in payload)
                {
                    writer.WriteLine(JsonSerializer.Serialize(item, CompactOptions));
                }

                break;
            case JsonOutputFormat.Compact:
                writer.WriteLine(JsonSerializer.Serialize(payload, CompactOptions));
                break;
            default:
                writer.WriteLine(JsonSerializer.Serialize(payload, PrettyOptions));
                break;
        }
    }

    private static object ToObject(MatchResult m, string cwd)
    {
        var relative = m.File;
        try
        {
            relative = Path.GetRelativePath(cwd, m.File).Replace('\\', '/');
        }
        catch
        {
            // keep absolute
        }

        var single = new Dictionary<string, object>(StringComparer.Ordinal);
        var multi = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in m.MetaVariables)
        {
            if (name is "_" or "")
            {
                continue;
            }

            if (value.IsMulti)
            {
                // ast-grep shape: multi[name] is an array of per-node entries (includes commas).
                var parts = value.Parts is { Count: > 0 }
                    ? value.Parts.Select(MetaEntryFromSpan).ToArray()
                    : new[] { MetaEntryFromValue(value) };
                multi[name] = parts;
            }
            else
            {
                single[name] = MetaEntryFromValue(value);
            }
        }

        static object MetaEntryFromValue(MetaVarValue value) =>
            new
            {
                text = value.Text,
                range = new
                {
                    byteOffset = new { start = value.ByteStart, end = value.ByteEnd },
                    start = new { line = value.StartLine0, column = value.StartColumn0 },
                    end = new { line = value.EndLine0, column = value.EndColumn0 },
                    charOffset = new { start = value.Start, end = value.End },
                },
            };

        static object MetaEntryFromSpan(MetaVarSpan span) =>
            new
            {
                text = span.Text,
                range = new
                {
                    byteOffset = new { start = span.ByteStart, end = span.ByteEnd },
                    start = new { line = span.StartLine0, column = span.StartColumn0 },
                    end = new { line = span.EndLine0, column = span.EndColumn0 },
                    charOffset = new { start = span.Start, end = span.End },
                },
            };

        return new
        {
            text = m.Text,
            range = new
            {
                byteOffset = new { start = m.ByteStart, end = m.ByteEnd },
                charOffset = new { start = m.Start, end = m.End },
                start = new { line = m.StartLine0, column = m.StartColumn0 },
                end = new { line = m.EndLine0, column = m.EndColumn0 },
            },
            file = relative,
            lines = m.Lines,
            language = m.Language,
            encoding = m.Encoding,
            ruleId = m.RuleId,
            message = m.Message,
            severity = m.Severity,
            metaVariables = new
            {
                single,
                multi,
                transformed = new Dictionary<string, string>(),
            },
        };
    }
}
