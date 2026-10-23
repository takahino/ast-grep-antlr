using System.Security.Cryptography;
using System.Text;
using AstGrepAntlr.Matching;

namespace AstGrepAntlr.Rewrite;

public sealed record RewriteChange(string File, string OriginalHash, string UnifiedDiff, string NewText);

public static class Rewriter
{
    public static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> meta, string matchedText)
    {
        var result = template;
        // Longer names first so $$$ARGS before $A if overlapping (won't usually)
        foreach (var kv in meta.OrderByDescending(k => k.Key.Length))
        {
            result = result.Replace("$" + kv.Key, kv.Value, StringComparison.Ordinal);
            result = result.Replace("$$$" + kv.Key, kv.Value, StringComparison.Ordinal);
        }

        return result.Replace("$$$", matchedText, StringComparison.Ordinal);
    }

    public static IEnumerable<MatchResult> DeduplicateOverlaps(IEnumerable<MatchResult> matches)
    {
        // Prefer outer (larger) ranges when overlapping.
        var list = matches.OrderBy(m => m.File).ThenBy(m => m.Start).ThenByDescending(m => m.End - m.Start).ToList();
        var accepted = new List<MatchResult>();
        foreach (var m in list)
        {
            if (accepted.Any(a => a.File == m.File && RangesOverlap(a.Start, a.End, m.Start, m.End)))
            {
                continue;
            }

            accepted.Add(m);
        }

        return accepted;
    }

    private static bool RangesOverlap(int a0, int a1, int b0, int b1) => a0 < b1 && b0 < a1;

    public static RewriteChange PreviewFile(
        string path,
        string original,
        IEnumerable<(MatchResult Match, string Replacement)> edits
    )
    {
        var ordered = edits.OrderByDescending(e => e.Match.Start).ToList();
        var text = original;
        foreach (var (match, replacement) in ordered)
        {
            if (match.Start < 0 || match.End > text.Length || match.Start > match.End)
            {
                continue;
            }

            text = string.Concat(text.AsSpan(0, match.Start), replacement, text.AsSpan(match.End));
        }

        var hash = Sha256Hex(original);
        var diff = MakeUnifiedDiff(path, original, text);
        return new RewriteChange(path, hash, diff, text);
    }

    public static void ApplyAtomic(string path, string newText, string expectedHash)
    {
        var current = File.ReadAllText(path);
        var hash = Sha256Hex(current);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"File '{path}' changed since preview (hash mismatch). Aborting apply."
            );
        }

        var tmp = path + ".ag4tmp";
        // Preserve BOM / newline roughly: write UTF8 without BOM by default; detect BOM from original bytes.
        var originalBytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(originalBytes);
        File.WriteAllText(tmp, newText, encoding);
        File.Move(tmp, path, overwrite: true);
    }

    private static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static string MakeUnifiedDiff(string path, string oldText, string newText)
    {
        if (oldText == newText)
        {
            return string.Empty;
        }

        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -1,{oldLines.Length} +1,{newLines.Length} @@");
        // Simple full-file diff for MVP (not LCS). Good enough for preview.
        foreach (var line in oldLines)
        {
            sb.Append('-').AppendLine(line);
        }

        foreach (var line in newLines)
        {
            sb.Append('+').AppendLine(line);
        }

        return sb.ToString();
    }
}
