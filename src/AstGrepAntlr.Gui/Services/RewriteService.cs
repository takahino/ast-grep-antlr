using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Text;

namespace AstGrepAntlr.Gui.Services;

/// <summary>Per-file rewrite preview; <see cref="Error"/> is set when the file cannot be rewritten.</summary>
public sealed record RewritePreview(
    string File,
    string DisplayPath,
    int EditCount,
    string Diff,
    string NewText,
    string OriginalHash,
    DetectedEncoding? Detected,
    string? Error
);

public static class RewriteService
{
    /// <summary>
    /// Builds diffs for every file with AST/rule hits. <paramref name="template"/> wins when non-empty;
    /// otherwise each hit's rule <c>fix</c> is used. Hits with neither are left untouched.
    /// </summary>
    public static IReadOnlyList<RewritePreview> Preview(
        IEnumerable<SearchHit> hits,
        string? template,
        string? explicitEncoding
    )
    {
        var previews = new List<RewritePreview>();
        foreach (var group in hits.Where(h => h.Match is not null).GroupBy(h => h.File))
        {
            var templates = new Dictionary<MatchResult, string>(ReferenceEqualityComparer.Instance);
            foreach (var hit in group)
            {
                var t = string.IsNullOrEmpty(template) ? hit.Fix : template;
                if (t is not null)
                {
                    templates[hit.Match!] = t;
                }
            }

            if (templates.Count == 0)
            {
                continue;
            }

            var display = group.First().DisplayPath;
            try
            {
                var source = SourceFile.Read(group.Key, explicitEncoding);
                var expectedHash = group.First().Match!.FileHash;
                // Offsets come from the search; if the file changed since, they no longer point at the matches.
                if (expectedHash is not null && !string.Equals(expectedHash, source.Hash, StringComparison.Ordinal))
                {
                    previews.Add(Failed(group.Key, display, "File changed since the search. Run the search again."));
                    continue;
                }

                var edits = Rewriter
                    .DeduplicateOverlaps(templates.Keys)
                    .Select(m => (m, Rewriter.ApplyTemplate(templates[m], m.MetaTexts, m.Text)))
                    .ToList();
                var change = Rewriter.PreviewFile(display, source, edits);
                previews.Add(
                    new RewritePreview(
                        group.Key,
                        display,
                        edits.Count,
                        change.UnifiedDiff,
                        change.NewText,
                        change.OriginalHash,
                        change.Detected,
                        null
                    )
                );
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
            {
                previews.Add(Failed(group.Key, display, ex.Message));
            }
        }

        return previews;
    }

    /// <summary>Writes one previewed file; aborts (throws) when the file changed after the preview.</summary>
    public static void Apply(RewritePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Error is not null)
        {
            throw new InvalidOperationException(preview.Error);
        }

        Rewriter.ApplyAtomic(preview.File, preview.NewText, preview.OriginalHash, preview.Detected);
    }

    private static RewritePreview Failed(string file, string display, string error) =>
        new(file, display, 0, "", "", "", null, error);
}
