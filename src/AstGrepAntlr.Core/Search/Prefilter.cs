namespace AstGrepAntlr.Search;

/// <summary>Mandatory literal check before the (far more expensive) parse.</summary>
public static class Prefilter
{
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
}
