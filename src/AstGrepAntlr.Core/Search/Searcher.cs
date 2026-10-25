using System.Collections.Concurrent;
using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Text;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Search;

public sealed class SearchOptions
{
    public required LanguageRegistry Registry { get; init; }
    public string? LanguageId { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = ["."];
    public int MaxResults { get; init; } = int.MaxValue;
    public bool ShowErrors { get; init; }
    public string? Encoding { get; init; }
    public ICollection<string>? MissingRoots { get; init; }
    public IReadOnlyList<string>? Globs { get; init; }
    public bool NoIgnore { get; init; }
    public bool IncludeHidden { get; init; }
    public string? BaseDirectory { get; init; }
    public int ContextLines { get; init; }
    public Dictionary<string, RuleNode>? Utils { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Named in-memory source for pattern search without touching the filesystem.</summary>
public readonly record struct SourceInput(string Path, string Content);

public sealed class Searcher
{
    private readonly TwoStageParser _parser = new();
    private readonly PatternCompiler _compiler = new();
    private readonly PatternMatcher _matcher = new();
    private readonly RuleEngine _rules = new();
    private readonly FileWalker _walker = new();

    public IEnumerable<MatchResult> RunPattern(
        string pattern,
        SearchOptions options,
        Strictness strictness = Strictness.Smart,
        string? entry = null
    )
    {
        var langId = options.LanguageId ?? throw new ArgumentException("Language is required for pattern search (-l).");
        var language = options.Registry.Get(langId);
        var compiled = _compiler.Compile(language, new PatternSpec(pattern, entry, Strictness: strictness));
        var files = _walker.Enumerate(
            options.Paths,
            new FileWalkOptions
            {
                Extensions = language.Extensions,
                Globs = options.Globs,
                NoIgnore = options.NoIgnore,
                IncludeHidden = options.IncludeHidden,
                BaseDirectory = options.BaseDirectory,
                MissingRoots = options.MissingRoots,
            }
        );
        return SearchFiles(
            files,
            language,
            compiled.RequiredLiterals,
            options,
            tree => _matcher.MatchAll(tree, compiled),
            ruleId: null,
            message: null,
            severity: null
        );
    }

    /// <summary>
    /// Pattern-search over caller-supplied source strings (e.g. frontend-collected buffers).
    /// <paramref name="sources"/> paths are labels only (appear in match <c>file</c>); nothing is read from disk.
    /// </summary>
    public IEnumerable<MatchResult> RunPatternOnSources(
        string pattern,
        IReadOnlyList<SourceInput> sources,
        SearchOptions options,
        Strictness strictness = Strictness.Smart,
        string? entry = null
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        var langId = options.LanguageId ?? throw new ArgumentException("Language is required for pattern search (-l).");
        var language = options.Registry.Get(langId);
        var compiled = _compiler.Compile(language, new PatternSpec(pattern, entry, Strictness: strictness));
        return SearchSources(
            sources,
            language,
            compiled.RequiredLiterals,
            options,
            tree => _matcher.MatchAll(tree, compiled),
            ruleId: null,
            message: null,
            severity: null
        );
    }

    public IEnumerable<MatchResult> RunRule(SgRuleDocument doc, SearchOptions options)
    {
        if (doc.Language is null || doc.Rule is null)
        {
            return [];
        }

        var language = options.Registry.Get(doc.Language);
        var files = _walker.Enumerate(
            options.Paths,
            new FileWalkOptions
            {
                Extensions = language.Extensions,
                Ignores = doc.Ignores,
                Files = doc.Files,
                Globs = options.Globs,
                NoIgnore = options.NoIgnore,
                IncludeHidden = options.IncludeHidden,
                BaseDirectory = options.BaseDirectory,
                MissingRoots = options.MissingRoots,
            }
        );
        var utils = options.Utils;
        return SearchFiles(
            files,
            language,
            _rules.RequiredLiterals(language, doc.Rule),
            options,
            tree => _rules.Match(language, tree, doc, utils),
            doc.Id,
            doc.Message,
            doc.Severity
        );
    }

    /// <summary>
    /// Walks <paramref name="files"/> in parallel, parsing only those that survive the literal
    /// prefilter, and projects whatever <paramref name="match"/> finds into ordered results.
    /// </summary>
    private IEnumerable<MatchResult> SearchFiles(
        IEnumerable<string> files,
        ILanguage language,
        IReadOnlyList<string> requiredLiterals,
        SearchOptions options,
        Func<SyntaxTree, IEnumerable<(int NodeIndex, Dictionary<string, MetaVarValue> Meta)>> match,
        string? ruleId,
        string? message,
        string? severity
    )
    {
        if (options.MaxResults >= int.MaxValue)
        {
            var bag = new ConcurrentBag<MatchResult>();
            Parallel.ForEach(
                files,
                new ParallelOptions
                {
                    CancellationToken = options.CancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                },
                file =>
                {
                    var parsed = TryParseFile(file, language, requiredLiterals, options);
                    if (parsed is null)
                    {
                        return;
                    }

                    CollectMatches(
                        bag,
                        file,
                        language.Id,
                        parsed,
                        match,
                        ruleId,
                        message,
                        severity,
                        options.ContextLines
                    );
                }
            );

            return OrderTake(bag, options.MaxResults);
        }

        var ordered = files.OrderBy(f => f, StringComparer.Ordinal).ToList();
        return SearchOrdered(
            ordered,
            options,
            (file, bag) =>
            {
                var parsed = TryParseFile(file, language, requiredLiterals, options);
                if (parsed is null)
                {
                    return;
                }

                CollectMatches(bag, file, language.Id, parsed, match, ruleId, message, severity, options.ContextLines);
            }
        );
    }

    private IEnumerable<MatchResult> SearchSources(
        IReadOnlyList<SourceInput> sources,
        ILanguage language,
        IReadOnlyList<string> requiredLiterals,
        SearchOptions options,
        Func<SyntaxTree, IEnumerable<(int NodeIndex, Dictionary<string, MetaVarValue> Meta)>> match,
        string? ruleId,
        string? message,
        string? severity
    )
    {
        if (options.MaxResults >= int.MaxValue)
        {
            var bag = new ConcurrentBag<MatchResult>();
            Parallel.ForEach(
                sources,
                new ParallelOptions
                {
                    CancellationToken = options.CancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                },
                input =>
                {
                    var path = string.IsNullOrEmpty(input.Path) ? "<buffer>" : input.Path;
                    var source = SourceFile.FromText(path, input.Content ?? "");
                    var parsed = TryParseSource(source, language, requiredLiterals, options);
                    if (parsed is null)
                    {
                        return;
                    }

                    CollectMatches(
                        bag,
                        path,
                        language.Id,
                        parsed,
                        match,
                        ruleId,
                        message,
                        severity,
                        options.ContextLines
                    );
                }
            );

            return OrderTake(bag, options.MaxResults);
        }

        var ordered = sources
            .Select(s =>
            {
                var path = string.IsNullOrEmpty(s.Path) ? "<buffer>" : s.Path;
                return (Path: path, Content: s.Content ?? "");
            })
            .OrderBy(s => s.Path, StringComparer.Ordinal)
            .ToList();

        return SearchOrdered(
            ordered,
            options,
            (item, bag) =>
            {
                var source = SourceFile.FromText(item.Path, item.Content);
                var parsed = TryParseSource(source, language, requiredLiterals, options);
                if (parsed is null)
                {
                    return;
                }

                CollectMatches(
                    bag,
                    item.Path,
                    language.Id,
                    parsed,
                    match,
                    ruleId,
                    message,
                    severity,
                    options.ContextLines
                );
            }
        );
    }

    /// <summary>
    /// Parallel search over Ordinal-ordered inputs with early stop once a contiguous prefix of
    /// completed items already covers <see cref="SearchOptions.MaxResults"/>.
    /// </summary>
    private static IEnumerable<MatchResult> SearchOrdered<T>(
        IReadOnlyList<T> ordered,
        SearchOptions options,
        Action<T, ConcurrentBag<MatchResult>> process
    )
    {
        var perIndex = new ConcurrentDictionary<int, List<MatchResult>>();
        var gate = new object();
        var nextComplete = 0;
        var prefixCount = 0;
        var max = options.MaxResults;

        var indexed = new (int Index, T Item)[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            indexed[i] = (i, ordered[i]);
        }

        Parallel.ForEach(
            Partitioner.Create(indexed, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions
            {
                CancellationToken = options.CancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            },
            (entry, state) =>
            {
                if (state.ShouldExitCurrentIteration)
                {
                    return;
                }

                var bag = new ConcurrentBag<MatchResult>();
                process(entry.Item, bag);
                var list = bag.OrderBy(m => m.Start).ToList();
                perIndex[entry.Index] = list;

                lock (gate)
                {
                    while (nextComplete < ordered.Count && perIndex.ContainsKey(nextComplete))
                    {
                        prefixCount += perIndex[nextComplete].Count;
                        nextComplete++;
                    }

                    if (prefixCount >= max)
                    {
                        state.Stop();
                    }
                }
            }
        );

        var results = new List<MatchResult>(Math.Min(max, prefixCount));
        for (var i = 0; i < nextComplete && results.Count < max; i++)
        {
            if (!perIndex.TryGetValue(i, out var list))
            {
                continue;
            }

            foreach (var m in list)
            {
                results.Add(m);
                if (results.Count >= max)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static void CollectMatches(
        ConcurrentBag<MatchResult> bag,
        string file,
        string languageId,
        ParsedFile parsed,
        Func<SyntaxTree, IEnumerable<(int NodeIndex, Dictionary<string, MetaVarValue> Meta)>> match,
        string? ruleId,
        string? message,
        string? severity,
        int contextLines
    )
    {
        // One line map per file; every match in it resolves by binary search.
        var lines = new LineMap(parsed.Tree.Source);
        var offsets = OffsetMap.ForSource(parsed.Source.Text, parsed.Source.Detected);
        foreach (var (node, env) in match(parsed.Tree))
        {
            bag.Add(
                ToMatch(
                    file,
                    languageId,
                    parsed.Tree,
                    lines,
                    offsets,
                    node,
                    env,
                    ruleId,
                    message,
                    severity,
                    parsed.Source,
                    contextLines
                )
            );
        }
    }

    private static IEnumerable<MatchResult> OrderTake(ConcurrentBag<MatchResult> bag, int maxResults) =>
        // Ordinal so results are identical across platforms and culture settings.
        bag.OrderBy(m => m.File, StringComparer.Ordinal).ThenBy(m => m.Start).Take(maxResults);

    /// <summary>Returns the parsed tree, or null when the file is skipped (too large, prefiltered out, unparseable).</summary>
    private ParsedFile? TryParseFile(
        string file,
        ILanguage language,
        IReadOnlyList<string> requiredLiterals,
        SearchOptions options
    )
    {
        if (language.MaxFileBytes > 0 && new FileInfo(file).Length > language.MaxFileBytes)
        {
            return null;
        }

        if (!SourceFile.TryRead(file, out var source, out var readError, options.Encoding))
        {
            if (options.ShowErrors)
            {
                Console.Error.WriteLine($"ag4: skip {file}: {readError}");
            }

            return null;
        }

        return TryParseSource(source!, language, requiredLiterals, options);
    }

    private ParsedFile? TryParseSource(
        SourceFile source,
        ILanguage language,
        IReadOnlyList<string> requiredLiterals,
        SearchOptions options
    )
    {
        if (language.MaxFileBytes > 0 && source.Bytes.Length > language.MaxFileBytes)
        {
            return null;
        }

        if (!Prefilter.MayContain(source.Text, requiredLiterals, language.IgnoreCase))
        {
            return null;
        }

        try
        {
            var tree = _parser
                .Parse(
                    language,
                    source.Text,
                    new LanguageParseOptions { CancellationToken = options.CancellationToken }
                )
                .Tree;
            return new ParsedFile(tree, source);
        }
        catch (Exception ex)
        {
            if (options.ShowErrors)
            {
                Console.Error.WriteLine($"ag4: parse failed {source.Path}: {ex.Message}");
            }

            return null;
        }
    }

    private static MatchResult ToMatch(
        string file,
        string lang,
        SyntaxTree tree,
        LineMap lines,
        OffsetMap offsets,
        int node,
        Dictionary<string, MetaVarValue> env,
        string? ruleId,
        string? message,
        string? severity,
        SourceFile source,
        int contextLines
    )
    {
        ref readonly var n = ref tree[node];
        var (sl, sc) = lines.Locate(n.Start);
        var (el, ec) = lines.Locate(n.End);
        var (byteStart, byteEnd) = offsets.Utf16RangeToBytes(n.Start, n.End);
        var ctxStartLine = Math.Max(1, sl - contextLines);
        var ctxEndLine = el + contextLines;
        var lineStart = lines.OffsetOfLine(ctxStartLine);
        var lineEnd = lines.OffsetOfLine(ctxEndLine + 1);
        if (lineEnd < 0)
        {
            lineEnd = tree.Source.Length;
        }

        var linesText = tree.Source[lineStart..Math.Min(lineEnd, tree.Source.Length)];
        if (linesText.EndsWith('\n') || linesText.EndsWith('\r'))
        {
            // keep trailing newline of last context line when present
        }

        return new MatchResult
        {
            File = file,
            Language = lang,
            Start = n.Start,
            End = n.End,
            ByteStart = byteStart,
            ByteEnd = byteEnd,
            StartLine = sl,
            StartColumn = sc,
            EndLine = el,
            EndColumn = ec,
            Text = tree.Text(node),
            MetaVariables = EnrichMeta(env, lines, offsets),
            RuleId = ruleId,
            Message = message,
            Severity = severity,
            Encoding = source.EncodingName,
            FileHash = source.Hash,
            Lines = linesText.TrimEnd('\r', '\n'),
        };
    }

    private static Dictionary<string, MetaVarValue> EnrichMeta(
        Dictionary<string, MetaVarValue> env,
        LineMap lines,
        OffsetMap offsets
    )
    {
        var result = new Dictionary<string, MetaVarValue>(env.Count, StringComparer.Ordinal);
        foreach (var (name, value) in env)
        {
            result[name] = EnrichOne(value, lines, offsets);
        }

        return result;
    }

    private static MetaVarValue EnrichOne(MetaVarValue value, LineMap lines, OffsetMap offsets)
    {
        var (bs, be) = offsets.Utf16RangeToBytes(value.Start, value.End);
        var (sl, sc) = lines.Locate(value.Start);
        var (el, ec) = lines.Locate(value.End);
        IReadOnlyList<MetaVarSpan>? parts = null;
        if (value.Parts is { Count: > 0 })
        {
            var enriched = new List<MetaVarSpan>(value.Parts.Count);
            foreach (var p in value.Parts)
            {
                var (pbs, pbe) = offsets.Utf16RangeToBytes(p.Start, p.End);
                var (psl, psc) = lines.Locate(p.Start);
                var (pel, pec) = lines.Locate(p.End);
                enriched.Add(
                    new MetaVarSpan(
                        p.Text,
                        p.Start,
                        p.End,
                        ByteStart: pbs,
                        ByteEnd: pbe,
                        StartLine0: Math.Max(0, psl - 1),
                        StartColumn0: Math.Max(0, psc - 1),
                        EndLine0: Math.Max(0, pel - 1),
                        EndColumn0: Math.Max(0, pec - 1)
                    )
                );
            }

            parts = enriched;
        }
        else if (value.IsMulti)
        {
            parts = value.Parts ?? [];
        }

        return value with
        {
            Parts = parts ?? value.Parts,
            ByteStart = bs,
            ByteEnd = be,
            StartLine0 = Math.Max(0, sl - 1),
            StartColumn0 = Math.Max(0, sc - 1),
            EndLine0 = Math.Max(0, el - 1),
            EndColumn0 = Math.Max(0, ec - 1),
        };
    }

    private sealed record ParsedFile(SyntaxTree Tree, SourceFile Source);
}

/// <summary>
/// Line/column lookup for one source text. Building the index costs a single scan, after which
/// each offset resolves by binary search instead of re-scanning from the start of the file.
/// </summary>
internal sealed class LineMap
{
    private readonly int[] _lineStarts;
    private readonly int _length;

    public LineMap(string source)
    {
        _length = source.Length;
        var starts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = starts.ToArray();
    }

    public (int Line, int Col) Locate(int offset)
    {
        offset = Math.Clamp(offset, 0, _length);
        var index = Array.BinarySearch(_lineStarts, offset);
        // A negative result is the bitwise complement of the next larger entry; the line
        // containing the offset is the one before it.
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (index + 1, offset - _lineStarts[index] + 1);
    }

    /// <summary>UTF-16 offset of the start of a 1-based line, or -1 if past the end.</summary>
    public int OffsetOfLine(int line1Based)
    {
        if (line1Based < 1)
        {
            return 0;
        }

        var index = line1Based - 1;
        if (index >= _lineStarts.Length)
        {
            return -1;
        }

        return _lineStarts[index];
    }
}
