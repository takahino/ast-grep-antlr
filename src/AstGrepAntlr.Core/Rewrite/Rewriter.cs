using System.Text;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Text;

namespace AstGrepAntlr.Rewrite;

public sealed record RewriteChange(
    string File,
    string OriginalHash,
    string UnifiedDiff,
    string NewText,
    DetectedEncoding Detected
);

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
        SourceFile source,
        IEnumerable<(MatchResult Match, string Replacement)> edits
    ) => PreviewFile(path, source.Text, source.Hash, source.Detected, edits);

    public static RewriteChange PreviewFile(
        string path,
        string original,
        IEnumerable<(MatchResult Match, string Replacement)> edits
    )
    {
        // Legacy overload: treat original as UTF-8 text hash (byte hash unknown).
        var detected = new DetectedEncoding(new System.Text.UTF8Encoding(false, true), "utf-8", false, 0);
        var hash = EncodingDetector.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(original));
        return PreviewFile(path, original, hash, detected, edits);
    }

    public static RewriteChange PreviewFile(
        string path,
        string original,
        string originalHash,
        DetectedEncoding detected,
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

        var diff = MakeUnifiedDiff(path, original, text);
        return new RewriteChange(path, originalHash, diff, text, detected);
    }

    /// <summary>
    /// Atomically write <paramref name="newText"/> using the encoding of the on-disk file,
    /// aborting if the raw-byte hash no longer matches <paramref name="expectedHash"/>.
    /// </summary>
    public static void ApplyAtomic(string path, string newText, string expectedHash, DetectedEncoding? detected = null)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = EncodingDetector.Sha256Hex(bytes);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"File '{path}' changed since preview (hash mismatch). Aborting apply."
            );
        }

        detected ??= EncodingDetector.Detect(bytes);
        byte[] encoded;
        try
        {
            encoded = EncodingDetector.Encode(newText, detected);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidOperationException(
                $"Cannot encode rewritten '{path}' as {detected.Name}: {ex.Message}",
                ex
            );
        }

        var tmp = path + ".ag4tmp";
        File.WriteAllBytes(tmp, encoded);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Load, preview, and optionally apply rewrites for one file.</summary>
    public static RewriteChange RewriteFile(
        string path,
        IEnumerable<MatchResult> matches,
        Func<MatchResult, string> template,
        bool apply,
        string? explicitEncoding = null
    )
    {
        var source = SourceFile.Read(path, explicitEncoding);
        var change = PreviewFile(path, source, DeduplicateOverlaps(matches).Select(m => (m, template(m))));
        if (apply)
        {
            ApplyAtomic(path, change.NewText, change.OriginalHash, change.Detected);
        }

        return change;
    }

    private static string MakeUnifiedDiff(string path, string oldText, string newText)
    {
        if (oldText == newText)
        {
            return string.Empty;
        }

        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
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
