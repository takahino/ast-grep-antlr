namespace AstGrepAntlr.Search;

public sealed class FileWalkOptions
{
    public IReadOnlyCollection<string>? Extensions { get; init; }
    public IReadOnlyList<string>? Ignores { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
    public IReadOnlyList<string>? Globs { get; init; }

    /// <summary>Base directory for resolving relative ignore/files globs (sgconfig dir or cwd).</summary>
    public string? BaseDirectory { get; init; }

    public bool NoIgnore { get; init; }
    public bool IncludeHidden { get; init; }
    public ICollection<string>? MissingRoots { get; init; }
}

public sealed class FileWalker
{
    private static readonly string[] DefaultSkipDirs = ["bin", "obj", "node_modules"];

    public IEnumerable<string> Enumerate(IEnumerable<string> roots, FileWalkOptions? options = null)
    {
        options ??= new FileWalkOptions();
        var baseDir = Path.GetFullPath(options.BaseDirectory ?? Directory.GetCurrentDirectory());
        var extSet =
            options.Extensions is null || options.Extensions.Count == 0
                ? null
                : new HashSet<string>(
                    options.Extensions.Select(e => e.StartsWith('.') ? e : "." + e),
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                var full = Path.GetFullPath(root);
                if (AcceptFile(full, baseDir, extSet, options, gitIgnore: null, gitRoot: null))
                {
                    yield return full;
                }

                continue;
            }

            if (!Directory.Exists(root))
            {
                options.MissingRoots?.Add(root);
                continue;
            }

            var walkRoot = Path.GetFullPath(root);
            GitIgnoreMatcher? gitIgnore = null;
            string? gitRoot = null;
            if (!options.NoIgnore)
            {
                gitRoot = FindGitRoot(walkRoot) ?? walkRoot;
                gitIgnore = LoadAncestorGitIgnore(walkRoot, gitRoot);
            }

            foreach (var file in WalkDirectory(walkRoot, gitRoot ?? walkRoot, gitIgnore, baseDir, extSet, options))
            {
                yield return file;
            }
        }
    }

    /// <summary>Backward-compatible overload used by existing call sites.</summary>
    public IEnumerable<string> Enumerate(
        IEnumerable<string> roots,
        IReadOnlyCollection<string>? extensions = null,
        IEnumerable<string>? ignores = null,
        ICollection<string>? missingRoots = null
    ) =>
        Enumerate(
            roots,
            new FileWalkOptions
            {
                Extensions = extensions,
                Ignores = ignores?.ToList(),
                MissingRoots = missingRoots,
            }
        );

    private static IEnumerable<string> WalkDirectory(
        string dir,
        string gitRoot,
        GitIgnoreMatcher? gitIgnore,
        string baseDir,
        HashSet<string>? extSet,
        FileWalkOptions options
    )
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        if (!options.NoIgnore)
        {
            var giPath = Path.Combine(dir, ".gitignore");
            if (File.Exists(giPath))
            {
                gitIgnore ??= new GitIgnoreMatcher();
                gitIgnore.AddFile(RelPath(gitRoot, dir), File.ReadAllLines(giPath));
            }
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (name is "." or "..")
            {
                continue;
            }

            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsHiddenName(name) && !options.IncludeHidden)
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                if (!options.NoIgnore && IsDefaultSkipDir(name))
                {
                    continue;
                }

                if (gitIgnore is not null)
                {
                    var relDir = RelPath(gitRoot, entry);
                    if (gitIgnore.IsDirectoryIgnored(relDir))
                    {
                        continue;
                    }
                }

                foreach (var file in WalkDirectory(entry, gitRoot, gitIgnore, baseDir, extSet, options))
                {
                    yield return file;
                }

                continue;
            }

            if (!File.Exists(entry))
            {
                continue;
            }

            if (AcceptFile(entry, baseDir, extSet, options, gitIgnore, gitRoot))
            {
                yield return Path.GetFullPath(entry);
            }
        }
    }

    private static bool AcceptFile(
        string fullPath,
        string baseDir,
        HashSet<string>? extSet,
        FileWalkOptions options,
        GitIgnoreMatcher? gitIgnore,
        string? gitRoot
    )
    {
        var name = Path.GetFileName(fullPath);
        if (IsHiddenName(name) && !options.IncludeHidden)
        {
            return false;
        }

        if (extSet is not null && !extSet.Contains(Path.GetExtension(fullPath)))
        {
            return false;
        }

        if (gitRoot is not null && gitIgnore is not null)
        {
            var rel = RelPath(gitRoot, fullPath);
            if (gitIgnore.IsIgnored(rel, isDirectory: false))
            {
                return false;
            }
        }

        var relToBase = RelPath(baseDir, fullPath);

        if (options.Files is { Count: > 0 } && !options.Files.Any(p => PathGlob.IsMatch(relToBase, p)))
        {
            return false;
        }

        if (options.Ignores is { Count: > 0 } && options.Ignores.Any(p => PathGlob.IsMatch(relToBase, p)))
        {
            return false;
        }

        if (!PathGlob.MatchesGlobs(relToBase, options.Globs))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads .gitignore from parents of <paramref name="walkRoot"/> up to <paramref name="gitRoot"/>,
    /// plus <c>.git/info/exclude</c>. The walk root's own file is loaded when that directory is visited.
    /// </summary>
    private static GitIgnoreMatcher LoadAncestorGitIgnore(string walkRoot, string gitRoot)
    {
        var matcher = new GitIgnoreMatcher();
        var chain = new List<string>();
        var cur = walkRoot;
        while (true)
        {
            var parent = Directory.GetParent(cur);
            if (parent is null)
            {
                break;
            }

            cur = parent.FullName;
            chain.Add(cur);
            if (PathsEqual(cur, gitRoot))
            {
                break;
            }
        }

        chain.Reverse();
        foreach (var dir in chain)
        {
            var gi = Path.Combine(dir, ".gitignore");
            if (File.Exists(gi))
            {
                matcher.AddFile(RelPath(gitRoot, dir), File.ReadAllLines(gi));
            }
        }

        var exclude = Path.Combine(gitRoot, ".git", "info", "exclude");
        if (File.Exists(exclude))
        {
            matcher.AddFile(string.Empty, File.ReadAllLines(exclude));
        }

        return matcher;
    }

    private static string? FindGitRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsDefaultSkipDir(string name)
    {
        foreach (var skipped in DefaultSkipDirs)
        {
            if (string.Equals(name, skipped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHiddenName(string name) => name.StartsWith('.');

    private static string RelPath(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (rel is "." or "")
        {
            return string.Empty;
        }

        return rel.TrimStart('/');
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
}
