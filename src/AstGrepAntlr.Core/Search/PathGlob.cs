namespace AstGrepAntlr.Search;

/// <summary>
/// Glob matcher shared by rule <c>files</c>/<c>ignores</c>, CLI <c>--globs</c>, and gitignore.
/// Supports <c>*</c>, <c>?</c>, <c>**</c>, and <c>[...]</c> character classes.
/// </summary>
public static class PathGlob
{
    /// <summary>
    /// Matches a relative path (forward slashes) against a glob pattern.
    /// When <paramref name="basenameOnlyFallback"/> is true and the pattern contains no
    /// <c>/</c>, also matches if the pattern matches any path segment (gitignore-style).
    /// </summary>
    public static bool IsMatch(string relativePath, string pattern, bool basenameOnlyFallback = false)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(pattern);

        var path = relativePath.Replace('\\', '/').Trim('/');
        var pat = pattern.Replace('\\', '/');
        if (pat.Length == 0)
        {
            return false;
        }

        var anchored = pat.StartsWith('/');
        if (anchored)
        {
            pat = pat[1..];
        }

        pat = pat.TrimEnd('/');
        if (pat.Length == 0)
        {
            return path.Length == 0;
        }

        if (anchored || pat.Contains('/'))
        {
            return MatchFull(path, pat);
        }

        var name = path;
        var slash = path.LastIndexOf('/');
        if (slash >= 0)
        {
            name = path[(slash + 1)..];
        }

        if (MatchFull(name, pat))
        {
            return true;
        }

        if (!basenameOnlyFallback)
        {
            return false;
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (MatchFull(segment, pat))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="relativePath"/> matches any include (or there are none) and
    /// no exclude. Exclude patterns are those starting with <c>!</c>.
    /// </summary>
    public static bool MatchesGlobs(string relativePath, IReadOnlyList<string>? globs)
    {
        if (globs is null || globs.Count == 0)
        {
            return true;
        }

        var includes = new List<string>();
        var excludes = new List<string>();
        foreach (var g in globs)
        {
            if (string.IsNullOrWhiteSpace(g))
            {
                continue;
            }

            if (g.StartsWith('!'))
            {
                var rest = g[1..];
                if (rest.Length > 0)
                {
                    excludes.Add(rest);
                }
            }
            else
            {
                includes.Add(g);
            }
        }

        if (includes.Count > 0 && !includes.Any(p => IsMatch(relativePath, p)))
        {
            return false;
        }

        return excludes.Count == 0 || !excludes.Any(p => IsMatch(relativePath, p));
    }

    private static bool MatchFull(string path, string pattern) => MatchRecursive(path.AsSpan(), pattern.AsSpan());

    private static bool MatchRecursive(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern)
    {
        var pi = 0;
        var gi = 0;
        while (gi < pattern.Length)
        {
            if (gi + 1 < pattern.Length && pattern[gi] == '*' && pattern[gi + 1] == '*')
            {
                gi += 2;
                if (gi < pattern.Length && pattern[gi] == '/')
                {
                    gi++;
                }

                if (gi >= pattern.Length)
                {
                    return true;
                }

                var rest = pattern[gi..];
                if (MatchRecursive(path[pi..], rest))
                {
                    return true;
                }

                for (var i = pi; i < path.Length; i++)
                {
                    if (path[i] == '/' && MatchRecursive(path[(i + 1)..], rest))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pattern[gi] == '*')
            {
                gi++;
                var rest = pattern[gi..];
                if (MatchRecursive(path[pi..], rest))
                {
                    return true;
                }

                for (var i = pi; i < path.Length; i++)
                {
                    if (path[i] == '/')
                    {
                        break;
                    }

                    if (MatchRecursive(path[(i + 1)..], rest))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pi >= path.Length)
            {
                return false;
            }

            if (pattern[gi] == '?')
            {
                if (path[pi] == '/')
                {
                    return false;
                }

                pi++;
                gi++;
                continue;
            }

            if (pattern[gi] == '[')
            {
                var close = -1;
                for (var k = gi + 1; k < pattern.Length; k++)
                {
                    if (pattern[k] == ']')
                    {
                        close = k;
                        break;
                    }
                }

                if (close < 0)
                {
                    if (path[pi] != '[')
                    {
                        return false;
                    }

                    pi++;
                    gi++;
                    continue;
                }

                if (!CharClassMatch(path[pi], pattern[(gi + 1)..close]))
                {
                    return false;
                }

                pi++;
                gi = close + 1;
                continue;
            }

            var expected = pattern[gi];
            if (expected == '\\' && gi + 1 < pattern.Length)
            {
                gi++;
                expected = pattern[gi];
            }

            if (path[pi] != expected)
            {
                return false;
            }

            pi++;
            gi++;
        }

        return pi == path.Length;
    }

    private static bool CharClassMatch(char c, ReadOnlySpan<char> clazz)
    {
        if (c == '/')
        {
            return false;
        }

        var negate = false;
        if (!clazz.IsEmpty && clazz[0] is '!' or '^')
        {
            negate = true;
            clazz = clazz[1..];
        }

        var matched = false;
        for (var i = 0; i < clazz.Length; i++)
        {
            if (i + 2 < clazz.Length && clazz[i + 1] == '-')
            {
                if (c >= clazz[i] && c <= clazz[i + 2])
                {
                    matched = true;
                    break;
                }

                i += 2;
                continue;
            }

            if (clazz[i] == c)
            {
                matched = true;
                break;
            }
        }

        return negate ? !matched : matched;
    }
}
