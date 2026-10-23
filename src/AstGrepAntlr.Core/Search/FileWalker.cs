namespace AstGrepAntlr.Search;

public sealed class FileWalker
{
    public IEnumerable<string> Enumerate(
        IEnumerable<string> roots,
        IReadOnlyCollection<string>? extensions = null,
        IEnumerable<string>? ignores = null
    )
    {
        var ignoreSet = new HashSet<string>(ignores ?? [], StringComparer.OrdinalIgnoreCase);
        var extSet =
            extensions is null || extensions.Count == 0
                ? null
                : new HashSet<string>(
                    extensions.Select(e => e.StartsWith('.') ? e : "." + e),
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                if (Accept(root, extSet, ignoreSet))
                {
                    yield return Path.GetFullPath(root);
                }

                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            var gitIgnores = LoadGitignores(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (IsUnderSkippedDirectory(file))
                {
                    continue;
                }

                if (IsGitIgnored(file, root, gitIgnores))
                {
                    continue;
                }

                if (Accept(file, extSet, ignoreSet))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool Accept(string path, HashSet<string>? extSet, HashSet<string> ignores)
    {
        if (extSet is not null && !extSet.Contains(Path.GetExtension(path)))
        {
            return false;
        }

        // A match on the file name is already a match on the full path, so only the path is checked.
        foreach (var ig in ignores)
        {
            if (path.Contains(ig, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "node_modules"];

    /// <summary>True when any path segment is a build/VCS directory we never search.</summary>
    private static bool IsUnderSkippedDirectory(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            foreach (var skipped in SkippedDirectories)
            {
                if (string.Equals(segment, skipped, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] LoadGitignores(string root)
    {
        var gi = Path.Combine(root, ".gitignore");
        if (!File.Exists(gi))
        {
            return [];
        }

        return File.ReadAllLines(gi).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
    }

    private static bool IsGitIgnored(string file, string root, string[] patterns)
    {
        if (patterns.Length == 0)
        {
            return false;
        }

        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        foreach (var p in patterns)
        {
            var pat = p.TrimStart('/');
            if (pat.EndsWith('/'))
            {
                if (rel.StartsWith(pat, StringComparison.Ordinal) || rel.Contains("/" + pat, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (
                rel.EndsWith(pat, StringComparison.Ordinal)
                || rel.Contains("/" + pat, StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(rel), pat, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }
}
