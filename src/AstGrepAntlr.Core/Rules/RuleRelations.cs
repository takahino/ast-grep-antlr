using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Rules;

/// <summary>Relational rule helpers: inside / has / precedes / follows with stopBy.</summary>
internal static class RuleRelations
{
    internal enum Kind
    {
        Inside,
        Has,
        Precedes,
        Follows,
    }

    private enum StopByMode
    {
        Neighbor,
        End,
        Rule,
    }

    internal static bool Holds(
        Func<
            ILanguage,
            SyntaxTree,
            RuleNode,
            Dictionary<string, RuleNode>,
            int,
            IEnumerable<(int NodeIndex, Dictionary<string, MetaVarValue> Meta)>
        > matchRule,
        ILanguage language,
        SyntaxTree tree,
        RuleNode relational,
        Dictionary<string, RuleNode> utils,
        int node,
        Kind kind
    )
    {
        var stopBy = relational.StopBy;
        var mode = ResolveStopByMode(stopBy);

        IEnumerable<int> candidates = kind switch
        {
            Kind.Inside => InsideCandidates(tree, node, mode, stopBy),
            Kind.Has => HasCandidates(tree, node, mode, stopBy),
            Kind.Precedes => SiblingCandidates(tree, node, forward: true, mode, stopBy),
            Kind.Follows => SiblingCandidates(tree, node, forward: false, mode, stopBy),
            _ => [],
        };

        foreach (var candidate in candidates)
        {
            var hits = matchRule(language, tree, relational, utils, candidate);
            var ok = kind is Kind.Precedes or Kind.Follows ? hits.Any() : hits.Any(m => m.NodeIndex == candidate);
            if (ok)
            {
                return true;
            }
        }

        return false;
    }

    private static StopByMode ResolveStopByMode(string? stopBy)
    {
        if (string.IsNullOrEmpty(stopBy) || string.Equals(stopBy, "neighbor", StringComparison.OrdinalIgnoreCase))
        {
            return StopByMode.Neighbor;
        }

        if (string.Equals(stopBy, "end", StringComparison.OrdinalIgnoreCase))
        {
            return StopByMode.End;
        }

        return StopByMode.Rule;
    }

    private static IEnumerable<int> InsideCandidates(SyntaxTree tree, int node, StopByMode mode, string? stopBy)
    {
        var parent = tree[node].Parent;
        if (parent < 0)
        {
            yield break;
        }

        if (mode == StopByMode.Neighbor)
        {
            yield return parent;
            yield break;
        }

        var p = parent;
        while (p >= 0)
        {
            yield return p;
            if (mode == StopByMode.Rule && stopBy is not null && tree.KindMatches(p, stopBy))
            {
                yield break;
            }

            p = tree[p].Parent;
        }
    }

    private static IEnumerable<int> HasCandidates(SyntaxTree tree, int node, StopByMode mode, string? stopBy)
    {
        if (mode == StopByMode.Neighbor)
        {
            foreach (var c in tree.NamedChildren(node))
            {
                yield return c;
            }

            yield break;
        }

        var stack = new Stack<int>(tree.NamedChildren(node).Reverse());
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            yield return c;
            if (mode == StopByMode.Rule && stopBy is not null && tree.KindMatches(c, stopBy))
            {
                continue;
            }

            foreach (var d in tree.NamedChildren(c).Reverse())
            {
                stack.Push(d);
            }
        }
    }

    private static IEnumerable<int> SiblingCandidates(
        SyntaxTree tree,
        int node,
        bool forward,
        StopByMode mode,
        string? stopBy
    )
    {
        // Climb through single-child wrappers (ANTLR expression → methodCall).
        var cursor = node;
        List<int> siblings;
        int idx;
        while (true)
        {
            var parent = tree[cursor].Parent;
            if (parent < 0)
            {
                yield break;
            }

            siblings = tree.NamedChildren(parent).ToList();
            idx = siblings.IndexOf(cursor);
            if (idx < 0)
            {
                idx = siblings.FindIndex(s => s == cursor || IsAncestorOf(tree, s, cursor));
            }

            if (idx >= 0 && siblings.Count > 1)
            {
                break;
            }

            if (idx >= 0 && siblings.Count == 1)
            {
                cursor = parent;
                continue;
            }

            yield break;
        }

        if (forward)
        {
            for (var i = idx + 1; i < siblings.Count; i++)
            {
                yield return siblings[i];
                if (mode == StopByMode.Neighbor)
                {
                    yield break;
                }

                if (mode == StopByMode.Rule && stopBy is not null && tree.KindMatches(siblings[i], stopBy))
                {
                    yield break;
                }
            }
        }
        else
        {
            for (var i = idx - 1; i >= 0; i--)
            {
                yield return siblings[i];
                if (mode == StopByMode.Neighbor)
                {
                    yield break;
                }

                if (mode == StopByMode.Rule && stopBy is not null && tree.KindMatches(siblings[i], stopBy))
                {
                    yield break;
                }
            }
        }
    }

    private static bool IsAncestorOf(SyntaxTree tree, int ancestor, int node)
    {
        var p = tree[node].Parent;
        while (p >= 0)
        {
            if (p == ancestor)
            {
                return true;
            }

            p = tree[p].Parent;
        }

        return false;
    }
}
