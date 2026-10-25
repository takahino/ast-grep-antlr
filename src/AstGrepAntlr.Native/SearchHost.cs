using System.Text.Json;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Search;

namespace AstGrepAntlr.Native;

internal static class SearchHost
{
    private static readonly Lazy<Language.LanguageRegistry> Registry = new(() => BuiltinLanguages.CreateRegistry());

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

        var max = req.MaxResults is > 0 ? req.MaxResults.Value : int.MaxValue;
        var strictness = Strictness.Smart;
        if (
            !string.IsNullOrWhiteSpace(req.Strictness)
            && !Enum.TryParse(req.Strictness, ignoreCase: true, out strictness)
        )
        {
            throw new ArgumentException($"Unknown strictness '{req.Strictness}'.");
        }

        if (!string.IsNullOrWhiteSpace(req.Backend))
        {
            if (!LanguageBackendNames.TryParse(req.Backend, out var backend))
            {
                throw new ArgumentException($"Unknown backend '{req.Backend}'.");
            }

            try
            {
                BuiltinLanguages.ApplyBackend(Registry.Value, req.Language!, backend);
            }
            catch (ArgumentException)
            {
                // Non-switchable language (csharp / plsql / …): ignore backend.
            }
        }

        List<MatchResult> matches;
        var useSources = req.Sources is { Length: > 0 };
        if (useSources)
        {
            var sources = new List<SourceInput>(req.Sources!.Length);
            for (var i = 0; i < req.Sources.Length; i++)
            {
                var s = req.Sources[i];
                if (s.Content is null)
                {
                    throw new ArgumentException($"sources[{i}].content is required.");
                }

                var label = string.IsNullOrWhiteSpace(s.Path) ? $"<buffer:{i}>" : s.Path!;
                sources.Add(new SourceInput(label, s.Content));
            }

            matches = new Searcher()
                .RunPatternOnSources(
                    req.Pattern!,
                    sources,
                    new SearchOptions
                    {
                        Registry = Registry.Value,
                        LanguageId = req.Language,
                        MaxResults = max,
                        NoIgnore = true,
                        IncludeHidden = true,
                    },
                    strictness
                )
                .ToList();
        }
        else
        {
            matches = new Searcher()
                .RunPattern(
                    req.Pattern!,
                    new SearchOptions
                    {
                        Registry = Registry.Value,
                        LanguageId = req.Language,
                        Paths = req.Paths is { Length: > 0 } ? req.Paths : ["."],
                        MaxResults = max,
                        NoIgnore = true,
                        IncludeHidden = true,
                    },
                    strictness
                )
                .ToList();
        }

        var cwd = Directory.GetCurrentDirectory();
        var dtos = matches.Select(m => ToDto(m, cwd, relativize: !useSources)).ToArray();
        return JsonSerializer.Serialize(dtos, NativeJsonContext.Default.MatchDtoArray);
    }

    private static MatchDto ToDto(MatchResult m, string cwd, bool relativize)
    {
        var relative = m.File;
        if (relativize)
        {
            try
            {
                relative = Path.GetRelativePath(cwd, m.File).Replace('\\', '/');
            }
            catch
            {
                // keep absolute
            }
        }

        Dictionary<string, MetaEntryDto>? single = null;
        Dictionary<string, MetaEntryDto[]>? multi = null;
        foreach (var (name, value) in m.MetaVariables)
        {
            if (name is "_" or "")
            {
                continue;
            }

            if (value.IsMulti)
            {
                multi ??= new Dictionary<string, MetaEntryDto[]>(StringComparer.Ordinal);
                multi[name] = value.Parts is { Count: > 0 }
                    ? value.Parts.Select(FromSpan).ToArray()
                    : [FromValue(value)];
            }
            else
            {
                single ??= new Dictionary<string, MetaEntryDto>(StringComparer.Ordinal);
                single[name] = FromValue(value);
            }
        }

        static MetaEntryDto FromValue(MetaVarValue value) =>
            new()
            {
                Text = value.Text,
                Range = new RangeDto
                {
                    ByteOffset = new OffsetPairDto { Start = value.ByteStart, End = value.ByteEnd },
                    CharOffset = new OffsetPairDto { Start = value.Start, End = value.End },
                    Start = new LineColDto { Line = value.StartLine0, Column = value.StartColumn0 },
                    End = new LineColDto { Line = value.EndLine0, Column = value.EndColumn0 },
                },
            };

        static MetaEntryDto FromSpan(MetaVarSpan span) =>
            new()
            {
                Text = span.Text,
                Range = new RangeDto
                {
                    ByteOffset = new OffsetPairDto { Start = span.ByteStart, End = span.ByteEnd },
                    CharOffset = new OffsetPairDto { Start = span.Start, End = span.End },
                    Start = new LineColDto { Line = span.StartLine0, Column = span.StartColumn0 },
                    End = new LineColDto { Line = span.EndLine0, Column = span.EndColumn0 },
                },
            };

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
