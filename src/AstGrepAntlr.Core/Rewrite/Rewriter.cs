using System.Text;
using System.Text.RegularExpressions;
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

public static partial class Rewriter
{
    [GeneratedRegex(@"\$\$\$([A-Z_][A-Z0-9_]*)|\$([A-Z_][A-Z0-9_]*)|\$\$\$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateMetaRegex();

    /// <summary>
    /// Substitute metavars in a rewrite template. Resolves <c>$$$NAME</c>, then <c>$NAME</c>, then bare <c>$$$</c>.
    /// </summary>
    public static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> meta, string matchedText)
    {
        return TemplateMetaRegex()
            .Replace(
                template,
                m =>
                {
                    if (m.Groups[1].Success)
                    {
                        var name = m.Groups[1].Value;
                        return meta.TryGetValue(name, out var v) ? v : m.Value;
                    }

                    if (m.Groups[2].Success)
                    {
                        var name = m.Groups[2].Value;
                        return meta.TryGetValue(name, out var v) ? v : m.Value;
                    }

                    return matchedText;
                }
            );
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

    private static string MakeUnifiedDiff(string path, string oldText, string newText) =>
        UnifiedDiff.Make(path, oldText, newText);
}
