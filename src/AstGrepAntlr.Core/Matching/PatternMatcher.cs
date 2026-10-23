using AstGrepAntlr.Pattern;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Matching;

public sealed class PatternMatcher
{
    public IEnumerable<(int NodeIndex, Dictionary<string, string> Meta)> MatchAll(
        SyntaxTree target,
        CompiledPattern pattern
    )
    {
        var rootKind = pattern.Tree[pattern.Root].TypeName;
        IEnumerable<int> candidates = pattern.Tree[pattern.Root].MetaVarName is not null
            ? Enumerable.Range(0, target.NodeCount)
            : target.NodesOfKind(rootKind).Concat(CandidatesByChain(target, rootKind)).Distinct();

        var seen = new HashSet<(int, int)>();
        foreach (var candidate in candidates)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!MatchNode(target, candidate, pattern.Tree, pattern.Root, pattern.Strictness, env))
            {
                continue;
            }

            var key = (target[candidate].Start, target[candidate].End);
            if (!seen.Add(key))
            {
                continue;
            }

            yield return (candidate, env);
        }
    }

    private static IEnumerable<int> CandidatesByChain(SyntaxTree target, string kind)
    {
        for (var i = 0; i < target.NodeCount; i++)
        {
            if (target.KindMatches(i, kind))
            {
                yield return i;
            }
        }
    }

    private static bool MatchNode(
        SyntaxTree target,
        int tIndex,
        SyntaxTree pattern,
        int pIndex,
        Strictness strictness,
        Dictionary<string, string> env
    )
    {
        var p = pattern[pIndex];
        var t = target[tIndex];

        if (p.MetaVarName is not null)
        {
            if (p.IsMultiMetaVar)
            {
                // Multi meta handled at sequence level.
                return true;
            }

            var text = target.Text(tIndex);
            if (p.MetaVarName is "_" or "")
            {
                return true;
            }

            if (env.TryGetValue(p.MetaVarName, out var existing))
            {
                return string.Equals(existing, text, StringComparison.Ordinal);
            }

            env[p.MetaVarName] = text;
            return true;
        }

        if (p.IsTerminal)
        {
            if (!t.IsTerminal)
            {
                // Descend into single-child chains on target.
                if (t.ChildCount == 1)
                {
                    return MatchNode(target, t.FirstChild, pattern, pIndex, strictness, env);
                }

                return false;
            }

            var cmp = StringComparison.Ordinal;
            return string.Equals(pattern.Text(pIndex), target.Text(tIndex), cmp)
                || string.Equals(p.TypeName, t.TypeName, StringComparison.Ordinal)
                    && string.Equals(pattern.Text(pIndex), target.Text(tIndex), cmp);
        }

        // Rule node: kind must match along chain for smart/ast.
        if (strictness != Strictness.Relaxed && !target.KindMatches(tIndex, p.TypeName) && t.TypeName != p.TypeName)
        {
            // Try unwrap single-child on either side.
            if (t.ChildCount == 1 && MatchNode(target, t.FirstChild, pattern, pIndex, strictness, env))
            {
                return true;
            }

            if (p.ChildCount == 1 && MatchNode(target, tIndex, pattern, p.FirstChild, strictness, env))
            {
                return true;
            }

            if (strictness is Strictness.Cst)
            {
                return false;
            }
        }

        return MatchChildren(target, tIndex, pattern, pIndex, strictness, env);
    }

    private static bool MatchChildren(
        SyntaxTree target,
        int tIndex,
        SyntaxTree pattern,
        int pIndex,
        Strictness strictness,
        Dictionary<string, string> env
    )
    {
        var pChildren = SelectChildren(pattern, pIndex, strictness).ToList();
        var tChildren = SelectChildren(target, tIndex, strictness).ToList();

        return MatchSequence(target, tChildren, pattern, pChildren, strictness, env, 0, 0);
    }

    private static IEnumerable<int> SelectChildren(SyntaxTree tree, int index, Strictness strictness)
    {
        return strictness switch
        {
            Strictness.Cst => tree.Children(index),
            Strictness.Ast => tree.NamedChildren(index),
            Strictness.Relaxed => tree.NamedChildren(index),
            _ => tree.Children(index), // smart: keep unnamed in pattern, skip extras in target
        };
    }

    private static bool MatchSequence(
        SyntaxTree target,
        List<int> tChildren,
        SyntaxTree pattern,
        List<int> pChildren,
        Strictness strictness,
        Dictionary<string, string> env,
        int ti,
        int pi
    )
    {
        while (pi < pChildren.Count && ti <= tChildren.Count)
        {
            var pNode = pattern[pChildren[pi]];
            if (pNode.IsMultiMetaVar || (pNode.MetaVarName is not null && pNode.IsMultiMetaVar))
            {
                // Lazy multi: consume until next pattern matches.
                var name = pNode.MetaVarName ?? string.Empty;
                pi++;
                if (pi >= pChildren.Count)
                {
                    // Rest of children
                    if (name is not ("_" or ""))
                    {
                        var texts = tChildren.Skip(ti).Select(target.Text);
                        var joined = string.Join(string.Empty, texts);
                        if (env.TryGetValue(name, out var existing) && existing != joined)
                        {
                            return false;
                        }

                        env[name] = joined;
                    }

                    return true;
                }

                for (var end = ti; end <= tChildren.Count; end++)
                {
                    var envCopy = new Dictionary<string, string>(env, StringComparer.Ordinal);
                    if (MatchSequence(target, tChildren, pattern, pChildren, strictness, envCopy, end, pi))
                    {
                        if (name is not ("_" or ""))
                        {
                            var joined = string.Join(
                                string.Empty,
                                tChildren.Skip(ti).Take(end - ti).Select(target.Text)
                            );
                            if (envCopy.TryGetValue(name, out var existing) && existing != joined)
                            {
                                continue;
                            }

                            envCopy[name] = joined;
                        }

                        foreach (var kv in envCopy)
                        {
                            env[kv.Key] = kv.Value;
                        }

                        return true;
                    }
                }

                return false;
            }

            if (ti >= tChildren.Count)
            {
                return false;
            }

            // smart: skip unnamed target tokens not present in pattern
            if (strictness == Strictness.Smart && !target[tChildren[ti]].IsNamed && pNode.IsNamed)
            {
                // If pattern expects named, skip unnamed target
                ti++;
                continue;
            }

            if (!MatchNode(target, tChildren[ti], pattern, pChildren[pi], strictness, env))
            {
                if (strictness == Strictness.Smart && !target[tChildren[ti]].IsNamed)
                {
                    ti++;
                    continue;
                }

                return false;
            }

            ti++;
            pi++;
        }

        if (pi < pChildren.Count)
        {
            return false;
        }

        // Trailing unnamed on target OK for smart/ast/relaxed
        while (ti < tChildren.Count)
        {
            if (strictness == Strictness.Cst)
            {
                return false;
            }

            if (strictness == Strictness.Smart && !target[tChildren[ti]].IsNamed)
            {
                ti++;
                continue;
            }

            if (strictness is Strictness.Ast or Strictness.Relaxed)
            {
                break;
            }

            return false;
        }

        return true;
    }
}
