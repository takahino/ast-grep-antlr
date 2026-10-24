namespace AstGrepAntlr.Search;

/// <summary>One gitignore pattern with its directory-relative base.</summary>
internal readonly struct GitIgnorePattern
{
    public GitIgnorePattern(string pattern, bool negate, bool directoryOnly, bool anchored)
    {
        Pattern = pattern;
        Negate = negate;
        DirectoryOnly = directoryOnly;
        Anchored = anchored;
    }

    public string Pattern { get; }
    public bool Negate { get; }
    public bool DirectoryOnly { get; }
    public bool Anchored { get; }
}

/// <summary>
/// Accumulates .gitignore / exclude rules and answers whether a relative path is ignored.
/// Last matching pattern wins (supports negation).
/// </summary>
public sealed class GitIgnoreMatcher
{
    private readonly List<(string BaseRel, GitIgnorePattern Pattern)> _rules = [];

    /// <summary>
    /// Adds patterns from a gitignore file. <paramref name="baseRelativeToRoot"/> is the directory
    /// containing the file, relative to the repository (or walk) root, using forward slashes
    /// (empty string for the root).
    /// </summary>
    public void AddFile(string baseRelativeToRoot, IEnumerable<string> lines)
    {
        var baseRel = (baseRelativeToRoot ?? string.Empty).Replace('\\', '/').Trim('/');
        foreach (var raw in lines)
        {
            if (!TryParseLine(raw, out var pattern))
            {
                continue;
            }

            _rules.Add((baseRel, pattern));
        }
    }

    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var path = relativePath.Replace('\\', '/').Trim('/');
        if (path.Length == 0)
        {
            return false;
        }

        bool? ignored = null;
        foreach (var (baseRel, rule) in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory)
            {
                if (!IsUnderDirectoryPattern(path, baseRel, rule))
                {
                    continue;
                }
            }
            else if (!Matches(path, baseRel, rule, isDirectory))
            {
                continue;
            }

            ignored = !rule.Negate;
        }

        return ignored == true;
    }

    /// <summary>True when a directory should not be entered because it (or an ancestor) is ignored.</summary>
    public bool IsDirectoryIgnored(string relativeDirPath) => IsIgnored(relativeDirPath, isDirectory: true);

    private static bool Matches(string path, string baseRel, GitIgnorePattern rule, bool isDirectory)
    {
        var rel = StripBase(path, baseRel);
        if (rel is null)
        {
            return false;
        }

        if (rule.DirectoryOnly && !isDirectory)
        {
            return false;
        }

        var pattern = rule.Anchored ? "/" + rule.Pattern : rule.Pattern;
        return PathGlob.IsMatch(rel, pattern, basenameOnlyFallback: !rule.Anchored && !rule.Pattern.Contains('/'));
    }

    private static bool IsUnderDirectoryPattern(string path, string baseRel, GitIgnorePattern rule)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acc = new List<string>();
        foreach (var part in parts)
        {
            acc.Add(part);
            var prefix = string.Join('/', acc);
            if (Matches(prefix, baseRel, rule, isDirectory: true))
            {
                return true;
            }
        }

        return false;
    }

    private static string? StripBase(string path, string baseRel)
    {
        if (baseRel.Length == 0)
        {
            return path;
        }

        if (path.Equals(baseRel, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (path.StartsWith(baseRel + "/", StringComparison.Ordinal))
        {
            return path[(baseRel.Length + 1)..];
        }

        return null;
    }

    internal static bool TryParseLine(string raw, out GitIgnorePattern pattern)
    {
        pattern = default;
        if (raw.Length == 0)
        {
            return false;
        }

        var line = StripUnescapedTrailingSpaces(raw);
        if (line.Length == 0 || line[0] == '#')
        {
            return false;
        }

        var negate = false;
        if (line[0] == '!')
        {
            negate = true;
            line = line[1..];
            if (line.Length == 0)
            {
                return false;
            }
        }

        var directoryOnly = false;
        if (line.EndsWith('/'))
        {
            directoryOnly = true;
            line = line[..^1];
        }

        var anchored = line.StartsWith('/');
        if (anchored)
        {
            line = line[1..];
        }

        if (line.Length == 0)
        {
            return false;
        }

        pattern = new GitIgnorePattern(line, negate, directoryOnly, anchored);
        return true;
    }

    private static string StripUnescapedTrailingSpaces(string s)
    {
        var end = s.Length;
        while (end > 0 && s[end - 1] == ' ')
        {
            var bs = 0;
            for (var i = end - 2; i >= 0 && s[i] == '\\'; i--)
            {
                bs++;
            }

            if (bs % 2 == 1)
            {
                break;
            }

            end--;
        }

        return s[..end];
    }
}
