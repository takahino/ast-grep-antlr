using System.Text.Json;

namespace AstGrepAntlr.DiffTest;

internal static class JsonMatchParser
{
    public static List<NormalizedMatch> ParseToolOutput(string stdout, string searchRoot)
    {
        var matches = new List<NormalizedMatch>();
        var trimmed = stdout.Trim();
        if (trimmed.Length == 0)
        {
            return matches;
        }

        // Prefer NDJSON / concatenated values; also accept a single JSON array.
        if (trimmed.StartsWith('[') && !trimmed.Contains("]\n[", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        TryAddMatch(element, searchRoot, matches);
                    }

                    return matches;
                }
            }
            catch (JsonException)
            {
                // Fall through to line / multi-document parsing.
            }
        }

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        TryAddMatch(element, searchRoot, matches);
                    }
                }
                else
                {
                    TryAddMatch(doc.RootElement, searchRoot, matches);
                }
            }
            catch (JsonException)
            {
                // Skip non-JSON lines (e.g. warnings).
            }
        }

        return matches;
    }

    private static void TryAddMatch(JsonElement element, string searchRoot, List<NormalizedMatch> matches)
    {
        if (!element.TryGetProperty("file", out var fileProp))
        {
            return;
        }

        var filePath = fileProp.GetString();
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        if (!element.TryGetProperty("range", out var range) || !range.TryGetProperty("byteOffset", out var byteOffset))
        {
            return;
        }

        var start = byteOffset.GetProperty("start").GetInt32();
        var end = byteOffset.GetProperty("end").GetInt32();
        var text = element.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
        var meta = ParseMetaVariables(element);
        var relative = ToRelativePath(filePath, searchRoot);
        matches.Add(new NormalizedMatch(relative, start, end, text, meta));
    }

    private static IReadOnlyDictionary<string, string> ParseMetaVariables(JsonElement element)
    {
        if (!element.TryGetProperty("metaVariables", out var metaRoot))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (metaRoot.ValueKind == JsonValueKind.Object)
        {
            if (metaRoot.TryGetProperty("single", out var single) && single.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in single.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("text", out var text))
                    {
                        result[prop.Name] = text.GetString() ?? "";
                    }
                }
            }

            if (metaRoot.TryGetProperty("multi", out var multi) && multi.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in multi.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        result[prop.Name] = string.Concat(
                            prop.Value.EnumerateArray().Select(item => item.GetProperty("text").GetString())
                        );
                    }
                }
            }

            if (result.Count == 0)
            {
                foreach (var prop in metaRoot.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("text", out var text))
                    {
                        result[prop.Name] = text.GetString() ?? "";
                    }
                }
            }
        }

        return result;
    }

    private static string ToRelativePath(string filePath, string searchRoot)
    {
        var fullFile = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(searchRoot);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        if (fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullFile[fullRoot.Length..].Replace('\\', '/');
        }

        return Path.GetFileName(fullFile).Replace('\\', '/');
    }
}
