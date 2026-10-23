namespace AstGrepAntlr.Search;

/// <summary>Byte-level mandatory literal check before parsing.</summary>
public static class Prefilter
{
    public static bool MayContain(ReadOnlySpan<byte> utf8, IReadOnlyList<string> requiredLiterals, bool ignoreCase)
    {
        if (requiredLiterals.Count == 0)
        {
            return true;
        }

        foreach (var lit in requiredLiterals)
        {
            if (string.IsNullOrEmpty(lit))
            {
                continue;
            }

            var needle = System.Text.Encoding.UTF8.GetBytes(lit);
            if (ignoreCase)
            {
                if (!ContainsIgnoreCase(utf8, needle))
                {
                    return false;
                }
            }
            else if (utf8.IndexOf(needle) < 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool MayContain(string text, IReadOnlyList<string> requiredLiterals, bool ignoreCase)
    {
        if (requiredLiterals.Count == 0)
        {
            return true;
        }

        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var lit in requiredLiterals)
        {
            if (string.IsNullOrEmpty(lit))
            {
                continue;
            }

            if (text.IndexOf(lit, cmp) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsIgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                var a = haystack[i + j];
                var b = needle[j];
                if (a >= (byte)'A' && a <= (byte)'Z')
                {
                    a = (byte)(a + 32);
                }

                if (b >= (byte)'A' && b <= (byte)'Z')
                {
                    b = (byte)(b + 32);
                }

                if (a != b)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return true;
            }
        }

        return false;
    }
}
