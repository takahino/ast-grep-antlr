using System.Globalization;
using System.Text;
using AstGrepAntlr.Gui.Models;

namespace AstGrepAntlr.Gui.Services;

/// <summary>Generated code-view document: text plus what to highlight and which hit each line belongs to.</summary>
public sealed record CodeViewDocument(
    string Text,
    IReadOnlyList<(int Start, int End)> MatchRanges,
    IReadOnlySet<int> HeaderLines,
    IReadOnlyDictionary<int, SearchHit> LineHits
)
{
    public static readonly CodeViewDocument Empty = new("", [], new HashSet<int>(), new Dictionary<int, SearchHit>());
}

/// <summary>
/// Renders hits grouped by file. Context windows of nearby hits in the same file are merged so a line
/// is printed once; gaps between windows are shown as "⋮".
/// </summary>
public static class CodeViewBuilder
{
    private const int LineNoWidth = 6;

    // ">" + line number + " │ " — every source line gets this fixed-width gutter.
    private static readonly int GutterWidth = 1 + LineNoWidth + 3;

    public static CodeViewDocument Build(IReadOnlyList<SearchHit> hits, int maxHits, string truncatedNote)
    {
        if (hits.Count == 0)
        {
            return CodeViewDocument.Empty;
        }

        var sb = new StringBuilder();
        var ranges = new List<(int, int)>();
        var headers = new HashSet<int>();
        var lineHits = new Dictionary<int, SearchHit>();
        var docLine = 1;
        var shown = hits.Take(maxHits).ToList();

        foreach (var group in shown.GroupBy(h => h.File))
        {
            var fileHits = group.OrderBy(h => h.Start).ToList();
            headers.Add(docLine);
            lineHits[docLine] = fileHits[0];
            AppendLine($"■ {fileHits[0].DisplayPath}  ({fileHits.Count})");

            // Union of all context lines in this file (line number → text).
            var lines = new SortedDictionary<int, string>();
            foreach (var hit in fileHits)
            {
                var n = hit.ContextStartLine;
                foreach (var text in hit.ContextText.Split('\n'))
                {
                    lines.TryAdd(n++, text.TrimEnd('\r'));
                }
            }

            // Sweep lines in order keeping only hits that span the current line, so files with many hits
            // stay linear instead of scanning every hit per line.
            var byStartLine = fileHits.OrderBy(h => h.StartLine).ToList();
            var next = 0;
            var active = new List<SearchHit>();
            var previous = -1;
            foreach (var (lineNo, text) in lines)
            {
                if (previous >= 0 && lineNo > previous + 1)
                {
                    AppendLine(new string(' ', GutterWidth - 2) + "⋮");
                }

                while (next < byStartLine.Count && byStartLine[next].StartLine <= lineNo)
                {
                    active.Add(byStartLine[next++]);
                }

                active.RemoveAll(h => h.EndLine < lineNo);
                var marker = active.Count == 0 ? ' ' : '>';
                var lineStartOffset = sb.Length;
                // Context-only lines link to the upcoming hit (or the last one) for double-click navigation.
                lineHits[docLine] =
                    active.Count > 0 ? active[0]
                    : next < byStartLine.Count ? byStartLine[next]
                    : byStartLine[^1];
                AppendLine($"{marker}{lineNo.ToString(CultureInfo.InvariantCulture).PadLeft(LineNoWidth)} │ {text}");

                foreach (var hit in active)
                {
                    var from = lineNo == hit.StartLine ? hit.StartColumn - 1 : 0;
                    var to = lineNo == hit.EndLine ? hit.EndColumn - 1 : text.Length;
                    from = Math.Clamp(from, 0, text.Length);
                    to = Math.Clamp(to, from, text.Length);
                    if (to > from)
                    {
                        ranges.Add((lineStartOffset + GutterWidth + from, lineStartOffset + GutterWidth + to));
                    }
                }

                previous = lineNo;
            }

            AppendLine("");
        }

        if (hits.Count > shown.Count)
        {
            AppendLine(string.Format(CultureInfo.CurrentCulture, truncatedNote, shown.Count));
        }

        ranges.Sort();
        return new CodeViewDocument(sb.ToString(), ranges, headers, lineHits);

        void AppendLine(string line)
        {
            sb.Append(line).Append('\n');
            docLine++;
        }
    }
}
