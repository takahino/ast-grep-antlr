using System.Text;
using System.Text.RegularExpressions;

namespace AstGrepAntlr.Tests.Harness;

/// <summary>Applies a unified diff (context hunks) to reconstruct new text from old.</summary>
public static partial class PatchOracle
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    public static string Apply(string oldText, string unifiedDiff)
    {
        if (string.IsNullOrEmpty(unifiedDiff))
        {
            return oldText;
        }

        var oldLines = SplitKeepLast(oldText);
        var result = new List<string>(oldLines);
        var lines = unifiedDiff.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        // Skip --- / +++ headers
        while (
            i < lines.Length
            && (
                lines[i].StartsWith("---", StringComparison.Ordinal)
                || lines[i].StartsWith("+++", StringComparison.Ordinal)
            )
        )
        {
            i++;
        }

        // Apply hunks from bottom to top so earlier line numbers stay valid.
        var hunks = new List<(int OldStart, List<(char Tag, string Text)> Body)>();
        while (i < lines.Length)
        {
            var m = HunkHeader().Match(lines[i]);
            if (!m.Success)
            {
                i++;
                continue;
            }

            var oldStart = int.Parse(m.Groups[1].Value) - 1;
            i++;
            var body = new List<(char, string)>();
            while (i < lines.Length && !lines[i].StartsWith("@@", StringComparison.Ordinal))
            {
                var line = lines[i];
                if (line.Length == 0)
                {
                    i++;
                    continue;
                }

                var tag = line[0];
                if (tag is ' ' or '-' or '+')
                {
                    body.Add((tag, line[1..]));
                }

                i++;
            }

            hunks.Add((oldStart, body));
        }

        for (var h = hunks.Count - 1; h >= 0; h--)
        {
            var (oldStart, body) = hunks[h];
            var removeCount = body.Count(b => b.Tag is ' ' or '-');
            var insert = body.Where(b => b.Tag is ' ' or '+').Select(b => b.Text).ToList();
            if (oldStart < 0 || oldStart + removeCount > result.Count)
            {
                throw new InvalidOperationException(
                    $"Hunk out of range: oldStart={oldStart} remove={removeCount} lines={result.Count}"
                );
            }

            result.RemoveRange(oldStart, removeCount);
            result.InsertRange(oldStart, insert);
        }

        return JoinKeepLast(result, oldText);
    }

    private static List<string> SplitKeepLast(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (normalized.Length == 0)
        {
            return [];
        }

        var parts = normalized.Split('\n').ToList();
        // Split always yields a trailing empty when text ends with \n; keep that as an empty line
        // only when the original had content after the last newline — Match unified-diff line count.
        return parts;
    }

    private static string JoinKeepLast(List<string> lines, string original)
    {
        var sb = new StringBuilder();
        var useCrLf = original.Contains("\r\n", StringComparison.Ordinal);
        var nl = useCrLf ? "\r\n" : "\n";
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(nl);
            }

            sb.Append(lines[i]);
        }

        return sb.ToString();
    }
}
