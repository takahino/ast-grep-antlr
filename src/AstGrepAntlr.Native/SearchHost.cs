using System.Text.Json;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Search;

namespace AstGrepAntlr.Native;

internal static class SearchHost
{
    private static readonly Lazy<Language.LanguageRegistry> Registry = new(BuiltinLanguages.CreateRegistry);

    public static string Search(string requestJson)
    {
        SearchRequest? req;
        try
        {
            req = JsonSerializer.Deserialize(requestJson, NativeJsonContext.Default.SearchRequest);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid request JSON: {ex.Message}", ex);
        }

        if (req is null)
        {
            throw new ArgumentException("Request JSON deserialized to null.");
        }

        if (string.IsNullOrWhiteSpace(req.Pattern))
        {
            throw new ArgumentException("pattern is required.");
        }

        if (string.IsNullOrWhiteSpace(req.Language))
        {
            throw new ArgumentException("language is required.");
        }

        var paths = req.Paths is { Length: > 0 } ? (IReadOnlyList<string>)req.Paths : ["."];
        var max = req.MaxResults is > 0 ? req.MaxResults.Value : int.MaxValue;
        var strictness = Strictness.Smart;
        if (
            !string.IsNullOrWhiteSpace(req.Strictness)
            && !Enum.TryParse(req.Strictness, ignoreCase: true, out strictness)
        )
        {
            throw new ArgumentException($"Unknown strictness '{req.Strictness}'.");
        }

        var matches = new Searcher()
            .RunPattern(
                req.Pattern!,
                new SearchOptions
                {
                    Registry = Registry.Value,
                    LanguageId = req.Language,
                    Paths = paths,
                    MaxResults = max,
                    NoIgnore = true,
                    IncludeHidden = true,
                },
                strictness
            )
            .ToList();

        var cwd = Directory.GetCurrentDirectory();
        var dtos = matches.Select(m => ToDto(m, cwd)).ToArray();
        return JsonSerializer.Serialize(dtos, NativeJsonContext.Default.MatchDtoArray);
    }

    private static MatchDto ToDto(MatchResult m, string cwd)
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

        Dictionary<string, MetaEntryDto>? single = null;
        Dictionary<string, MetaEntryDto[]>? multi = null;
        foreach (var (name, value) in m.MetaVariables)
        {
            if (name is "_" or "")
            {
                continue;
            }

            var entry = new MetaEntryDto
            {
                Text = value.Text,
                CharOffset = new OffsetPairDto { Start = value.Start, End = value.End },
            };
            if (value.IsMulti)
            {
                multi ??= new Dictionary<string, MetaEntryDto[]>(StringComparer.Ordinal);
                multi[name] = [entry];
            }
            else
            {
                single ??= new Dictionary<string, MetaEntryDto>(StringComparer.Ordinal);
                single[name] = entry;
            }
        }

        return new MatchDto
        {
            Text = m.Text,
            File = relative,
            Language = m.Language,
            Range = new RangeDto
            {
                ByteOffset = new OffsetPairDto { Start = m.ByteStart, End = m.ByteEnd },
                CharOffset = new OffsetPairDto { Start = m.Start, End = m.End },
                Start = new LineColDto { Line = m.StartLine0, Column = m.StartColumn0 },
                End = new LineColDto { Line = m.EndLine0, Column = m.EndColumn0 },
            },
            MetaVariables =
                single is null && multi is null ? null : new MetaVariablesDto { Single = single, Multi = multi },
            RuleId = m.RuleId,
            Message = m.Message,
            Severity = m.Severity,
            Lines = m.Lines,
            Encoding = m.Encoding,
        };
    }
}
