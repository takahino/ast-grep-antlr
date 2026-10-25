using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Search;
using AstGrepAntlr.Text;

namespace AstGrepAntlr.Gui.Services;

/// <summary>Plain-text / regex search (no parsing). Reuses ag4's file walker and encoding detection.</summary>
internal static class TextSearch
{
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private const int BinarySniffBytes = 8000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static List<SearchHit> Run(
        SearchRequest req,
        ResolvedRoot root,
        int limit,
        List<string> warnings,
        CancellationToken ct
    )
    {
        var regex = BuildRegex(req);
        var config = SearchService.FindConfig(req, root);
        var encoding = SearchService.EffectiveEncoding(req, config);
        var files = new FileWalker()
            .Enumerate(
                [root.Path],
                new FileWalkOptions
                {
                    Globs = SearchService.BuildGlobs(req),
                    NoIgnore = req.NoIgnore,
                    IncludeHidden = req.IncludeHidden,
                    BaseDirectory = root.BaseDir,
                }
            )
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var perFile = new ConcurrentDictionary<int, List<SearchHit>>();
        var fileWarnings = new ConcurrentQueue<string>();
        var total = 0;
        Parallel.For(
            0,
            files.Count,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            (i, state) =>
            {
                var found = SearchFile(files[i], root, regex, req.ContextLines, encoding, fileWarnings);
                if (found.Count == 0)
                {
                    return;
                }

                perFile[i] = found;
                // Over-collecting slightly is fine; the caller trims to the limit.
                if (Interlocked.Add(ref total, found.Count) > limit)
                {
                    state.Stop();
                }
            }
        );

        warnings.AddRange(fileWarnings);
        return perFile.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).Take(SafeTake(limit)).ToList();
    }

    public static Regex BuildRegex(SearchRequest req)
    {
        var body = req.Mode == SearchMode.Regex ? req.Pattern : Regex.Escape(req.Pattern);
        if (req.Mode == SearchMode.Text && req.WholeWord)
        {
            body = $@"\b{body}\b";
        }

        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!req.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(body, options, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regular expression: {ex.Message}", ex);
        }
    }

    private static List<SearchHit> SearchFile(
        string file,
        ResolvedRoot root,
        Regex regex,
        int contextLines,
        string? encoding,
        ConcurrentQueue<string> warnings
    )
    {
        SourceFile? source;
        try
        {
            if (new FileInfo(file).Length > MaxFileBytes)
            {
                return [];
            }

            if (!SourceFile.TryRead(file, out source, out _, encoding) || source is null)
            {
                return [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Enqueue($"{file}: {ex.Message}");
            return [];
        }

        if (!source.Detected.HasBom && LooksBinary(source.Bytes))
        {
            return [];
        }

        var text = source.Text;
        var lines = new LineIndex(text);
        var hits = new List<SearchHit>();
        try
        {
            foreach (Match m in regex.Matches(text))
            {
                if (m.Length == 0)
                {
                    continue;
                }

                var (sl, sc) = lines.Locate(m.Index);
                var (el, ec) = lines.Locate(m.Index + m.Length);
                var ctxStart = Math.Max(1, sl - Math.Max(0, contextLines));
                var ctxEnd = Math.Min(lines.LineCount, el + Math.Max(0, contextLines));
                hits.Add(
                    new SearchHit
                    {
                        File = file,
                        DisplayPath = SearchService.DisplayPath(root, file),
                        Language = "text",
                        StartLine = sl,
                        StartColumn = sc,
                        EndLine = el,
                        EndColumn = ec,
                        Start = m.Index,
                        End = m.Index + m.Length,
                        Text = m.Value,
                        ContextStartLine = ctxStart,
                        ContextText = lines.Slice(ctxStart, ctxEnd),
                        MetaVariables = Groups(m),
                        Encoding = source.EncodingName,
                    }
                );
            }
        }
        catch (RegexMatchTimeoutException)
        {
            warnings.Enqueue($"{file}: regex timed out; file skipped");
            return [];
        }

        return hits;
    }

    private static Dictionary<string, string> Groups(Match m)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var g = 1; g < m.Groups.Count; g++)
        {
            if (m.Groups[g].Success)
            {
                result[m.Groups[g].Name] = m.Groups[g].Value;
            }
        }

        return result;
    }

    private static bool LooksBinary(byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniffBytes)) >= 0;

    private static int SafeTake(int limit) => limit >= int.MaxValue - 1 ? int.MaxValue : limit + 1;
}

/// <summary>Line start table for offset → (line, column) lookups; both 1-based.</summary>
internal sealed class LineIndex
{
    private readonly string _text;
    private readonly List<int> _starts = [0];

    public LineIndex(string text)
    {
        _text = text;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _starts.Add(i + 1);
            }
        }
    }

    public int LineCount => _starts.Count;

    public (int Line, int Column) Locate(int offset)
    {
        var idx = _starts.BinarySearch(offset);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }

        return (idx + 1, offset - _starts[idx] + 1);
    }

    /// <summary>Text of lines <paramref name="first"/>..<paramref name="last"/> (inclusive), without the final newline.</summary>
    public string Slice(int first, int last)
    {
        var start = _starts[first - 1];
        var end = last < _starts.Count ? _starts[last] : _text.Length;
        return _text[start..end].TrimEnd('\r', '\n');
    }
}
