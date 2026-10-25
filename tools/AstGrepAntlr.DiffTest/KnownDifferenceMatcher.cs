namespace AstGrepAntlr.DiffTest;

internal static class KnownDifferenceMatcher
{
    public static bool IsExplained(
        KnownDifferencesFile known,
        string language,
        string patternId,
        string repoId,
        MatchBucket bucket,
        string? ag4Backend = null
    )
    {
        if (bucket == MatchBucket.Same)
        {
            return true;
        }

        foreach (var entry in known.Differences)
        {
            if (
                !string.IsNullOrWhiteSpace(entry.Language)
                && !string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(entry.Backend)
                && !string.IsNullOrWhiteSpace(ag4Backend)
                && !string.Equals(entry.Backend, ag4Backend, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            // Untagged grammar-parse / grammar-boundary entries historically described
            // ANTLR CST vs upstream tree-sitter. When ag4 also uses tree-sitter, ignore them
            // unless they are explicitly tagged backend: tree-sitter (or antlr).
            if (
                string.IsNullOrWhiteSpace(entry.Backend)
                && !string.IsNullOrWhiteSpace(ag4Backend)
                && ag4Backend.Equals("tree-sitter", StringComparison.OrdinalIgnoreCase)
                && language is "java" or "cpp" or "python" or "csharp"
                && entry.Category is not null
                && (
                    entry.Category.Contains("grammar-parse", StringComparison.OrdinalIgnoreCase)
                    || entry.Category.Contains("grammar-boundary", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(entry.PatternId)
                && !string.Equals(entry.PatternId, patternId, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(entry.RepoId)
                && !string.Equals(entry.RepoId, repoId, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(entry.Bucket)
                && !string.Equals(entry.Bucket, bucket.ToString(), StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
