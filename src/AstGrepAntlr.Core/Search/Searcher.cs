using System.Collections.Concurrent;
using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Search;

public sealed class SearchOptions
{
    public required LanguageRegistry Registry { get; init; }
    public string? LanguageId { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = ["."];
    public int MaxResults { get; init; } = int.MaxValue;
    public bool ShowErrors { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

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
        var files = _walker.Enumerate(options.Paths, language.Extensions);
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

    public IEnumerable<MatchResult> RunRule(SgRuleDocument doc, SearchOptions options)
    {
        if (doc.Language is null || doc.Rule is null)
        {
            return [];
        }

        var language = options.Registry.Get(doc.Language);
        var files = _walker.Enumerate(options.Paths, language.Extensions, doc.Ignores);
        return SearchFiles(
            files,
            language,
            _rules.RequiredLiterals(language, doc.Rule),
            options,
            tree => _rules.Match(language, tree, doc),
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
        Func<SyntaxTree, IEnumerable<(int NodeIndex, Dictionary<string, string> Meta)>> match,
        string? ruleId,
        string? message,
        string? severity
    )
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
                var tree = TryParseFile(file, language, requiredLiterals, options);
                if (tree is null)
                {
                    return;
                }

                // One line map per file; every match in it resolves by binary search.
                var lines = new LineMap(tree.Source);
                foreach (var (node, env) in match(tree))
                {
                    bag.Add(ToMatch(file, language.Id, tree, lines, node, env, ruleId, message, severity));
                }
            }
        );

        // Ordinal so results are identical across platforms and culture settings.
        return bag.OrderBy(m => m.File, StringComparer.Ordinal).ThenBy(m => m.Start).Take(options.MaxResults);
    }

    /// <summary>Returns the parsed tree, or null when the file is skipped (too large, prefiltered out, unparseable).</summary>
    private SyntaxTree? TryParseFile(
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

        // ReadAllText strips BOM; do not use UTF8.GetString(ReadAllBytes) which keeps U+FEFF.
        var text = File.ReadAllText(file);
        if (!Prefilter.MayContain(text, requiredLiterals, language.IgnoreCase))
        {
            return null;
        }

        try
        {
            return _parser
                .Parse(language, text, new LanguageParseOptions { CancellationToken = options.CancellationToken })
                .Tree;
        }
        catch (Exception ex)
        {
            if (options.ShowErrors)
            {
                Console.Error.WriteLine($"ag4: parse failed {file}: {ex.Message}");
            }

            return null;
        }
    }

    private static MatchResult ToMatch(
        string file,
        string lang,
        SyntaxTree tree,
        LineMap lines,
        int node,
        Dictionary<string, string> env,
        string? ruleId,
        string? message,
        string? severity
    )
    {
        var n = tree[node];
        var (sl, sc) = lines.Locate(n.Start);
        var (el, ec) = lines.Locate(n.End);
        return new MatchResult
        {
            File = file,
            Language = lang,
            Start = n.Start,
            End = n.End,
            StartLine = sl,
            StartColumn = sc,
            EndLine = el,
            EndColumn = ec,
            Text = tree.Text(node),
            MetaVariables = env,
            RuleId = ruleId,
            Message = message,
            Severity = severity,
        };
    }
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
}
