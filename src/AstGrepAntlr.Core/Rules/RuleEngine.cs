using System.Text.RegularExpressions;
using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Rules;

public sealed class RuleEngine
{
    private readonly PatternCompiler _compiler = new();
    private readonly PatternMatcher _matcher = new();

    public IEnumerable<(int NodeIndex, Dictionary<string, string> Meta)> Match(
        ILanguage language,
        SyntaxTree tree,
        SgRuleDocument doc,
        Dictionary<string, RuleNode>? utils = null
    )
    {
        if (doc.Rule is null)
        {
            yield break;
        }

        var utilsMap = utils ?? doc.Utils ?? new Dictionary<string, RuleNode>();
        foreach (var (node, env) in MatchRule(language, tree, doc.Rule, utilsMap, -1))
        {
            if (doc.Constraints is not null)
            {
                var ok = true;
                foreach (var (name, constraint) in doc.Constraints)
                {
                    if (!env.TryGetValue(name, out var text))
                    {
                        ok = false;
                        break;
                    }

                    // Constraint applies to the captured text re-parsed as a tiny tree is expensive;
                    // for kind/regex constraints, check against the matched node text / kind roughly.
                    if (!ConstraintHolds(tree, node, text, constraint))
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                {
                    continue;
                }
            }

            yield return (node, env);
        }
    }

    private IEnumerable<(int, Dictionary<string, string>)> MatchRule(
        ILanguage language,
        SyntaxTree tree,
        RuleNode rule,
        Dictionary<string, RuleNode> utils,
        int scopedNode
    )
    {
        if (rule.All is { Count: > 0 })
        {
            // Intersection of matches by range
            List<(int, Dictionary<string, string>)>? acc = null;
            foreach (var child in rule.All)
            {
                var part = MatchRule(language, tree, child, utils, scopedNode).ToList();
                acc = acc is null
                    ? part
                    : acc.Where(a =>
                            part.Any(p =>
                                tree[p.Item1].Start == tree[a.Item1].Start && tree[p.Item1].End == tree[a.Item1].End
                            )
                        )
                        .ToList();
            }

            foreach (var x in acc ?? [])
            {
                yield return x;
            }

            yield break;
        }

        if (rule.Any is { Count: > 0 })
        {
            var seen = new HashSet<(int, int)>();
            foreach (var child in rule.Any)
            {
                foreach (var m in MatchRule(language, tree, child, utils, scopedNode))
                {
                    var key = (tree[m.Item1].Start, tree[m.Item1].End);
                    if (seen.Add(key))
                    {
                        yield return m;
                    }
                }
            }

            yield break;
        }

        if (rule.Not is not null)
        {
            var inner = MatchRule(language, tree, rule.Not, utils, scopedNode)
                .Select(m => (tree[m.Item1].Start, tree[m.Item1].End))
                .ToHashSet();
            for (var i = 0; i < tree.NodeCount; i++)
            {
                if (scopedNode >= 0 && (tree[i].Start < tree[scopedNode].Start || tree[i].End > tree[scopedNode].End))
                {
                    continue;
                }

                if (!inner.Contains((tree[i].Start, tree[i].End)))
                {
                    yield return (i, new Dictionary<string, string>());
                }
            }

            yield break;
        }

        if (rule.Matches is not null && utils.TryGetValue(rule.Matches, out var utilRule))
        {
            foreach (var m in MatchRule(language, tree, utilRule, utils, scopedNode))
            {
                yield return m;
            }

            yield break;
        }

        IEnumerable<(int, Dictionary<string, string>)> seeds;
        if (rule.Pattern is not null || rule.PatternObject is not null)
        {
            var spec = ToPatternSpec(rule);
            var compiled = _compiler.Compile(language, spec);
            seeds = _matcher.MatchAll(tree, compiled);
        }
        else if (rule.Kind is not null)
        {
            seeds = tree.NodesOfKind(rule.Kind)
                .Concat(Enumerable.Range(0, tree.NodeCount).Where(i => tree.KindMatches(i, rule.Kind)))
                .Distinct()
                .Select(i => (i, new Dictionary<string, string>()));
        }
        else if (rule.Regex is not null)
        {
            var rx = new Regex(rule.Regex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            seeds = Enumerable
                .Range(0, tree.NodeCount)
                .Where(i => tree[i].IsNamed && rx.IsMatch(tree.Text(i)))
                .Select(i => (i, new Dictionary<string, string>()));
        }
        else
        {
            yield break;
        }

        foreach (var (node, env) in seeds)
        {
            if (scopedNode >= 0 && (tree[node].Start < tree[scopedNode].Start || tree[node].End > tree[scopedNode].End))
            {
                continue;
            }

            if (rule.Inside is not null)
            {
                var ancestors = Ancestors(tree, node).ToList();
                var insideOk = false;
                foreach (var a in ancestors)
                {
                    foreach (var _ in MatchRule(language, tree, rule.Inside, utils, a))
                    {
                        insideOk = true;
                        break;
                    }

                    if (insideOk)
                    {
                        break;
                    }

                    if (string.Equals(rule.Inside.StopBy, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        // keep walking
                    }
                    else if (rule.Inside.StopBy is not null && tree.KindMatches(a, rule.Inside.StopBy))
                    {
                        break;
                    }
                }

                if (!insideOk)
                {
                    continue;
                }
            }

            if (rule.Has is not null)
            {
                var hasAny = MatchRule(language, tree, rule.Has, utils, node).Any();
                if (!hasAny)
                {
                    continue;
                }
            }

            yield return (node, env);
        }
    }

    private static PatternSpec ToPatternSpec(RuleNode rule)
    {
        if (rule.PatternObject is { } po)
        {
            var strictness = Enum.TryParse<Strictness>(po.Strictness, ignoreCase: true, out var s)
                ? s
                : Strictness.Smart;
            return new PatternSpec(
                po.Text ?? po.Context ?? string.Empty,
                po.Entry,
                po.Context,
                po.Selector,
                strictness
            );
        }

        return new PatternSpec(rule.Pattern ?? string.Empty);
    }

    private static IEnumerable<int> Ancestors(SyntaxTree tree, int node)
    {
        var p = tree[node].Parent;
        while (p >= 0)
        {
            yield return p;
            p = tree[p].Parent;
        }
    }

    private static bool ConstraintHolds(SyntaxTree tree, int node, string capturedText, RuleNode constraint)
    {
        if (constraint.Kind is not null)
        {
            // Find a descendant with this text and matching kind, or the node itself.
            if (tree.KindMatches(node, constraint.Kind) && tree.Text(node) == capturedText)
            {
                return true;
            }

            foreach (var d in tree.Descendants(node))
            {
                if (tree.Text(d) == capturedText && tree.KindMatches(d, constraint.Kind))
                {
                    return true;
                }
            }

            return false;
        }

        if (constraint.Regex is not null)
        {
            return Regex.IsMatch(capturedText, constraint.Regex);
        }

        return true;
    }
}
