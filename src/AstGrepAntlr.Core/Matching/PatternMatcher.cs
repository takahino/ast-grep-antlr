using AstGrepAntlr.Pattern;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Matching;

public sealed class PatternMatcher
{
    public IEnumerable<(int NodeIndex, Dictionary<string, MetaVarValue> Meta)> MatchAll(
        SyntaxTree target,
        CompiledPattern pattern
    )
    {
        var rootKind = pattern.Tree[pattern.Root].TypeName;
        IEnumerable<int> candidates = pattern.Tree[pattern.Root].MetaVarName is not null
            ? Enumerable.Range(0, target.NodeCount)
            : target.NodesMatchingKind(rootKind);

        var seen = new HashSet<(int, int)>();
        foreach (var candidate in candidates)
        {
            var env = new Dictionary<string, MetaVarValue>(StringComparer.Ordinal);
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

    private static bool MatchNode(
        SyntaxTree target,
        int tIndex,
        SyntaxTree pattern,
        int pIndex,
        Strictness strictness,
        Dictionary<string, MetaVarValue> env
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

            // B6: if the pattern metavar wraps a single child of kind K, reject targets whose
            // same-kind node has two or more K children (e.g. expressionList matched by $A).
            if (IsSingleChildListMismatch(pattern, pIndex, target, tIndex))
            {
                return false;
            }

            if (p.MetaVarTypedRule is not null && !target.KindMatches(tIndex, p.MetaVarTypedRule))
            {
                return false;
            }

            var value = new MetaVarValue(text, t.Start, t.End, IsMulti: false);
            if (env.TryGetValue(p.MetaVarName, out var existing))
            {
                return string.Equals(existing.Text, text, StringComparison.Ordinal);
            }

            env[p.MetaVarName] = value;
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

            return string.Equals(pattern.Text(pIndex), target.Text(tIndex), StringComparison.Ordinal);
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
        Dictionary<string, MetaVarValue> env
    )
    {
        var pChildren = SelectChildren(pattern, pIndex, strictness).ToList();
        var tChildren = SelectChildren(target, tIndex, strictness).ToList();

        return MatchSequence(target, tChildren, pattern, pChildren, strictness, env, 0, 0);
    }

    private static IEnumerable<int> SelectChildren(SyntaxTree tree, int index, Strictness strictness)
    {
        // Smart (default) and Cst keep unnamed tokens; Smart skips the extras on the target side
        // later, in MatchSequence. Ast/Relaxed drop them up front.
        return strictness is Strictness.Ast or Strictness.Relaxed ? tree.NamedChildren(index) : tree.Children(index);
    }

    private static bool MatchSequence(
        SyntaxTree target,
        List<int> tChildren,
        SyntaxTree pattern,
        List<int> pChildren,
        Strictness strictness,
        Dictionary<string, MetaVarValue> env,
        int ti,
        int pi
    )
    {
        while (pi < pChildren.Count && ti <= tChildren.Count)
        {
            var pNode = pattern[pChildren[pi]];
            if (pNode.IsMultiMetaVar)
            {
                // Lazy multi: consume until next pattern matches.
                var name = pNode.MetaVarName ?? string.Empty;
                pi++;
                if (pi >= pChildren.Count)
                {
                    // Rest of children
                    if (name is not ("_" or ""))
                    {
                        var captured = CaptureSlice(target, tChildren, ti, tChildren.Count);
                        if (env.TryGetValue(name, out var existing) && existing.Text != captured.Text)
                        {
                            return false;
                        }

                        env[name] = captured;
                    }

                    return true;
                }

                for (var end = ti; end <= tChildren.Count; end++)
                {
                    var envCopy = new Dictionary<string, MetaVarValue>(env, StringComparer.Ordinal);
                    if (MatchSequence(target, tChildren, pattern, pChildren, strictness, envCopy, end, pi))
                    {
                        if (name is not ("_" or ""))
                        {
                            var captured = CaptureSlice(target, tChildren, ti, end);
                            if (envCopy.TryGetValue(name, out var existing) && existing.Text != captured.Text)
                            {
                                continue;
                            }

                            envCopy[name] = captured;
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

    /// <summary>Capture $$$ as a contiguous source slice from first to last child (preserves separators).</summary>
    private static MetaVarValue CaptureSlice(SyntaxTree target, List<int> tChildren, int from, int to)
    {
        if (from >= to || from >= tChildren.Count)
        {
            // Empty capture: place at the boundary of the neighbor if any.
            var pos =
                from < tChildren.Count ? target[tChildren[from]].Start
                : to > 0 && to - 1 < tChildren.Count ? target[tChildren[to - 1]].End
                : 0;
            return new MetaVarValue(string.Empty, pos, pos, IsMulti: true);
        }

        var start = target[tChildren[from]].Start;
        var end = target[tChildren[to - 1]].End;
        var text = start >= 0 && end >= start && end <= target.Source.Length ? target.Source[start..end] : string.Empty;
        return new MetaVarValue(text, start, end, IsMulti: true);
    }

    /// <summary>
    /// B6 list guard: pattern metavar node has exactly one named child of kind K, but the target
    /// node of the same kind has two or more named children of kind K.
    /// </summary>
    private static bool IsSingleChildListMismatch(SyntaxTree pattern, int pIndex, SyntaxTree target, int tIndex)
    {
        var pNamed = pattern.NamedChildren(pIndex).ToList();
        if (pNamed.Count != 1)
        {
            return false;
        }

        var childKind = pattern[pNamed[0]].TypeName;
        if (string.IsNullOrEmpty(childKind))
        {
            return false;
        }

        // Only apply when the target node itself matches the pattern metavar node's kind (or chain).
        if (
            !string.Equals(pattern[pIndex].TypeName, target[tIndex].TypeName, StringComparison.Ordinal)
            && !target.KindMatches(tIndex, pattern[pIndex].TypeName)
        )
        {
            return false;
        }

        var tCount = 0;
        foreach (var c in target.NamedChildren(tIndex))
        {
            if (
                string.Equals(target[c].TypeName, childKind, StringComparison.Ordinal)
                || target.KindMatches(c, childKind)
            )
            {
                tCount++;
                if (tCount >= 2)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
