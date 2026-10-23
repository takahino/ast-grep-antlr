namespace AstGrepAntlr.DiffTest;

internal static class MatchComparer
{
    public static List<ClassifiedMatch> Classify(
        IReadOnlyList<NormalizedMatch> astGrep,
        IReadOnlyList<NormalizedMatch> ag4
    )
    {
        var astByRange = astGrep.GroupBy(m => m.RangeKey).ToDictionary(g => g.Key, g => g.First());
        var ag4ByRange = ag4.GroupBy(m => m.RangeKey).ToDictionary(g => g.Key, g => g.First());

        var results = new List<ClassifiedMatch>();
        var usedAst = new HashSet<string>();
        var usedAg4 = new HashSet<string>();

        foreach (var key in astByRange.Keys.Intersect(ag4ByRange.Keys))
        {
            var left = astByRange[key];
            var right = ag4ByRange[key];
            usedAst.Add(key);
            usedAg4.Add(key);

            if (MetaEquals(left.MetaVariables, right.MetaVariables) && left.Text == right.Text)
            {
                results.Add(new ClassifiedMatch(MatchBucket.Same, left, right));
            }
            else if (left.Text == right.Text)
            {
                results.Add(new ClassifiedMatch(MatchBucket.MetaVarDiff, left, right));
            }
            else
            {
                results.Add(new ClassifiedMatch(MatchBucket.RangeDiff, left, right));
            }
        }

        var astByText = astGrep.Where(m => !usedAst.Contains(m.RangeKey)).GroupBy(m => m.TextKey).ToList();
        var ag4ByText = ag4.Where(m => !usedAg4.Contains(m.RangeKey)).GroupBy(m => m.TextKey).ToList();
        var ag4TextLookup = ag4ByText.ToDictionary(g => g.Key, g => g.ToList());

        foreach (var astGroup in astByText)
        {
            if (!ag4TextLookup.TryGetValue(astGroup.Key, out var ag4Candidates) || ag4Candidates.Count == 0)
            {
                continue;
            }

            var pairs = Math.Min(astGroup.Count(), ag4Candidates.Count);
            for (var i = 0; i < pairs; i++)
            {
                var left = astGroup.ElementAt(i);
                var right = ag4Candidates[i];
                usedAst.Add(left.RangeKey);
                usedAg4.Add(right.RangeKey);

                if (left.ByteStart == right.ByteStart && left.ByteEnd == right.ByteEnd)
                {
                    results.Add(new ClassifiedMatch(MatchBucket.MetaVarDiff, left, right));
                }
                else
                {
                    results.Add(new ClassifiedMatch(MatchBucket.RangeDiff, left, right));
                }
            }
        }

        foreach (var m in astGrep)
        {
            if (!usedAst.Contains(m.RangeKey))
            {
                results.Add(new ClassifiedMatch(MatchBucket.OnlyAstGrep, m, null));
            }
        }

        foreach (var m in ag4)
        {
            if (!usedAg4.Contains(m.RangeKey))
            {
                results.Add(new ClassifiedMatch(MatchBucket.OnlyAg4, null, m));
            }
        }

        return results;
    }

    private static bool MetaEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
