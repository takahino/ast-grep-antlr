using System.Text;

namespace AstGrepAntlr.Rewrite;

/// <summary>Line-oriented LCS diff producing unified-diff hunks with context.</summary>
public static class UnifiedDiff
{
    public static string Make(string path, string oldText, string newText, int context = 3)
    {
        if (oldText == newText)
        {
            return string.Empty;
        }

        var nl = oldText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var a = SplitLines(oldText);
        var b = SplitLines(newText);
        var edits = Diff(a, b);
        var hunks = BuildHunks(edits, context);
        if (hunks.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var rel = path.Replace('\\', '/');
        sb.Append("--- a/").Append(rel).Append(nl);
        sb.Append("+++ b/").Append(rel).Append(nl);
        foreach (var h in hunks)
        {
            sb.Append("@@ -")
                .Append(h.OldStart)
                .Append(',')
                .Append(h.OldCount)
                .Append(" +")
                .Append(h.NewStart)
                .Append(',')
                .Append(h.NewCount)
                .Append(" @@")
                .Append(nl);
            foreach (var line in h.Lines)
            {
                sb.Append(line.Tag).Append(line.Text).Append(nl);
            }
        }

        return sb.ToString();
    }

    private static List<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        return normalized.Length == 0 ? [] : normalized.Split('\n').ToList();
    }

    private enum Kind
    {
        Equal,
        Delete,
        Insert,
    }

    private sealed record Edit(Kind Kind, string Text, int OldIndex, int NewIndex);

    private sealed record HunkLine(char Tag, string Text);

    private sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount, List<HunkLine> Lines);

    private static List<Edit> Diff(List<string> a, List<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var edits = new List<Edit>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                edits.Add(new Edit(Kind.Equal, a[x], x, y));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                edits.Add(new Edit(Kind.Delete, a[x], x, y));
                x++;
            }
            else
            {
                edits.Add(new Edit(Kind.Insert, b[y], x, y));
                y++;
            }
        }

        while (x < n)
        {
            edits.Add(new Edit(Kind.Delete, a[x], x, y));
            x++;
        }

        while (y < m)
        {
            edits.Add(new Edit(Kind.Insert, b[y], x, y));
            y++;
        }

        return edits;
    }

    private static List<Hunk> BuildHunks(List<Edit> edits, int context)
    {
        var changeIdx = new List<int>();
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Kind != Kind.Equal)
            {
                changeIdx.Add(i);
            }
        }

        if (changeIdx.Count == 0)
        {
            return [];
        }

        var regions = new List<(int Start, int End)>();
        var start = Math.Max(0, changeIdx[0] - context);
        var end = Math.Min(edits.Count, changeIdx[0] + 1 + context);
        for (var i = 1; i < changeIdx.Count; i++)
        {
            var cs = Math.Max(0, changeIdx[i] - context);
            var ce = Math.Min(edits.Count, changeIdx[i] + 1 + context);
            if (cs <= end)
            {
                end = ce;
            }
            else
            {
                regions.Add((start, end));
                start = cs;
                end = ce;
            }
        }

        regions.Add((start, end));

        var hunks = new List<Hunk>();
        foreach (var (rs, re) in regions)
        {
            var lines = new List<HunkLine>();
            var oldCount = 0;
            var newCount = 0;
            var oldStart = 0;
            var newStart = 0;
            var seen = false;
            for (var i = rs; i < re; i++)
            {
                var e = edits[i];
                if (!seen)
                {
                    oldStart = e.OldIndex + 1;
                    newStart = e.NewIndex + 1;
                    seen = true;
                }

                switch (e.Kind)
                {
                    case Kind.Equal:
                        lines.Add(new HunkLine(' ', e.Text));
                        oldCount++;
                        newCount++;
                        break;
                    case Kind.Delete:
                        lines.Add(new HunkLine('-', e.Text));
                        oldCount++;
                        break;
                    case Kind.Insert:
                        lines.Add(new HunkLine('+', e.Text));
                        newCount++;
                        break;
                }
            }

            if (oldCount == 0)
            {
                oldStart = edits[rs].OldIndex;
            }

            if (newCount == 0)
            {
                newStart = edits[rs].NewIndex;
            }

            hunks.Add(new Hunk(Math.Max(0, oldStart), oldCount, Math.Max(0, newStart), newCount, lines));
        }

        return hunks;
    }
}
