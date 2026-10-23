namespace AstGrepAntlr.Search;

public sealed class FileWalker
{
    public IEnumerable<string> Enumerate(
        IEnumerable<string> roots,
        IReadOnlyCollection<string>? extensions = null,
        IEnumerable<string>? globs = null,
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
                if (
                    IsUnder(file, ".git")
                    || IsUnder(file, "bin")
                    || IsUnder(file, "obj")
                    || IsUnder(file, "node_modules")
                )
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

        var name = Path.GetFileName(path);
        foreach (var ig in ignores)
        {
            if (
                name.Contains(ig, StringComparison.OrdinalIgnoreCase)
                || path.Contains(ig, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnder(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => string.Equals(p, segment, StringComparison.OrdinalIgnoreCase));

    private static List<(string Dir, string[] Patterns)> LoadGitignores(string root)
    {
        var list = new List<(string, string[])>();
        var gi = Path.Combine(root, ".gitignore");
        if (File.Exists(gi))
        {
            var patterns = File.ReadAllLines(gi)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToArray();
            list.Add((root, patterns));
        }

        return list;
    }

    private static bool IsGitIgnored(string file, string root, List<(string Dir, string[] Patterns)> gitIgnores)
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        foreach (var (_, patterns) in gitIgnores)
        {
            foreach (var p in patterns)
            {
                var pat = p.TrimStart('/');
                if (pat.EndsWith('/'))
                {
                    if (
                        rel.StartsWith(pat, StringComparison.Ordinal)
                        || rel.Contains("/" + pat, StringComparison.Ordinal)
                    )
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
        }

        return false;
    }
}
