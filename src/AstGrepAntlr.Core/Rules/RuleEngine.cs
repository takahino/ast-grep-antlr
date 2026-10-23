using System.Collections.Concurrent;
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

    // A rule's patterns are file-independent, so compiling them once and reusing the result
    // keeps a scan over N files from paying N parser constructions per pattern.
    private readonly ConcurrentDictionary<(string LanguageId, PatternSpec Spec), CompiledPattern> _compiled = new();

    /// <summary>
    /// Literals that must appear in a file for <paramref name="rule"/> to have any chance of
    /// matching, used to skip parsing entirely. Composite rules that can match without their
    /// pattern (any/not/matches) contribute nothing, so the filter stays sound.
    /// </summary>
    public IReadOnlyList<string> RequiredLiterals(ILanguage language, RuleNode rule)
    {
        if (rule.All is { Count: > 0 } all)
        {
            // Every branch must match, so each one's literals are required too.
            return all.SelectMany(child => RequiredLiterals(language, child)).Distinct(StringComparer.Ordinal).ToList();
        }

        if (rule.Any is { Count: > 0 } || rule.Not is not null || rule.Matches is not null)
        {
            return [];
        }

        if (rule.Pattern is null && rule.PatternObject is null)
        {
            return [];
        }

        try
        {
            return CompilePattern(language, ToPatternSpec(rule)).RequiredLiterals;
        }
        catch (InvalidOperationException)
        {
            // Pattern does not compile for this language; let the matcher report it per file.
            return [];
        }
    }

    private CompiledPattern CompilePattern(ILanguage language, PatternSpec spec) =>
        _compiled.GetOrAdd((language.Id, spec), key => _compiler.Compile(language, key.Spec));

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

    private IEnumerable<(int NodeIndex, Dictionary<string, string> Meta)> MatchRule(
        ILanguage language,
        SyntaxTree tree,
        RuleNode rule,
        Dictionary<string, RuleNode> utils,
        int scopedNode
    )
    {
        if (rule.All is { Count: > 0 })
        {
            // Matches are identified by source range, so `all` is the intersection of its branches.
            List<(int NodeIndex, Dictionary<string, string> Meta)>? acc = null;
            foreach (var child in rule.All)
            {
                var part = MatchRule(language, tree, child, utils, scopedNode).ToList();
                if (acc is null)
                {
                    acc = part;
                    continue;
                }

                var spans = part.Select(p => Span(tree, p.NodeIndex)).ToHashSet();
                acc = acc.Where(a => spans.Contains(Span(tree, a.NodeIndex))).ToList();
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
                    if (seen.Add(Span(tree, m.NodeIndex)))
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
                .Select(m => Span(tree, m.NodeIndex))
                .ToHashSet();
            for (var i = 0; i < tree.NodeCount; i++)
            {
                if (IsOutsideScope(tree, i, scopedNode))
                {
                    continue;
                }

                if (!inner.Contains(Span(tree, i)))
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

        IEnumerable<(int NodeIndex, Dictionary<string, string> Meta)> seeds;
        if (rule.Pattern is not null || rule.PatternObject is not null)
        {
            seeds = _matcher.MatchAll(tree, CompilePattern(language, ToPatternSpec(rule)));
        }
        else if (rule.Kind is not null)
        {
            seeds = tree.NodesMatchingKind(rule.Kind).Select(i => (i, new Dictionary<string, string>()));
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
            if (IsOutsideScope(tree, node, scopedNode))
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

    /// <summary>Source range of a node — how a match is identified across rule branches.</summary>
    private static (int Start, int End) Span(SyntaxTree tree, int node) => (tree[node].Start, tree[node].End);

    /// <summary>True when the node falls outside the enclosing node a nested rule is scoped to.</summary>
    private static bool IsOutsideScope(SyntaxTree tree, int node, int scopedNode) =>
        scopedNode >= 0 && (tree[node].Start < tree[scopedNode].Start || tree[node].End > tree[scopedNode].End);

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
