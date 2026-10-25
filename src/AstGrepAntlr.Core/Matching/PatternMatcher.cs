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
        ref readonly var patternRoot = ref pattern.Tree[pattern.Root];
        var rootKind = patternRoot.TypeName;
        IEnumerable<int> candidates = patternRoot.MetaVarName is not null
            ? Enumerable.Range(0, target.NodeCount)
            : target.NodesMatchingKind(rootKind);

        var seen = new HashSet<(int, int)>();
        var env = new Dictionary<string, MetaVarValue>(StringComparer.Ordinal);
        var undo = new List<EnvUndo>();
        foreach (var candidate in candidates)
        {
            env.Clear();
            undo.Clear();
            if (!MatchNode(target, candidate, pattern.Tree, pattern.Root, pattern.Strictness, env, undo))
            {
                continue;
            }

            ref readonly var c = ref target[candidate];
            var key = (c.Start, c.End);
            if (!seen.Add(key))
            {
                continue;
            }

            yield return (candidate, env);
            // Caller may retain the yielded dictionary; allocate a fresh one for the next candidate.
            env = new Dictionary<string, MetaVarValue>(StringComparer.Ordinal);
            undo = new List<EnvUndo>();
        }
    }

    private static bool MatchNode(
        SyntaxTree target,
        int tIndex,
        SyntaxTree pattern,
        int pIndex,
        Strictness strictness,
        Dictionary<string, MetaVarValue> env,
        List<EnvUndo> undo
    )
    {
        ref readonly var p = ref pattern[pIndex];
        ref readonly var t = ref target[tIndex];

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

            Bind(env, undo, p.MetaVarName, value);
            return true;
        }

        if (p.IsTerminal)
        {
            if (!t.IsTerminal)
            {
                // Descend into single-child chains on target.
                if (t.ChildCount == 1)
                {
                    return MatchNode(target, t.FirstChild, pattern, pIndex, strictness, env, undo);
                }

                return false;
            }

            var pText = pattern.Text(pIndex);
            var tText = target.Text(tIndex);
            // Grammars that keep whitespace as parser tokens (VB6 WS/NEWLINE): any run of
            // whitespace matches any other, like the whitespace between tokens elsewhere.
            return string.Equals(pText, tText, StringComparison.Ordinal)
                || (
                    pText.Length > 0
                    && tText.Length > 0
                    && string.IsNullOrWhiteSpace(pText)
                    && string.IsNullOrWhiteSpace(tText)
                );
        }

        // Rule node: kind must match along chain for smart/ast.
        if (strictness != Strictness.Relaxed && !target.KindMatches(tIndex, p.TypeName) && t.TypeName != p.TypeName)
        {
            // Try unwrap single-child on either side.
            if (t.ChildCount == 1 && MatchNode(target, t.FirstChild, pattern, pIndex, strictness, env, undo))
            {
                return true;
            }

            if (p.ChildCount == 1 && MatchNode(target, tIndex, pattern, p.FirstChild, strictness, env, undo))
            {
                return true;
            }

            if (strictness is Strictness.Cst)
            {
                return false;
            }
        }

        return MatchChildren(target, tIndex, pattern, pIndex, strictness, env, undo);
    }

    private static bool MatchChildren(
        SyntaxTree target,
        int tIndex,
        SyntaxTree pattern,
        int pIndex,
        Strictness strictness,
        Dictionary<string, MetaVarValue> env,
        List<EnvUndo> undo
    )
    {
        var pChildren = new List<int>();
        var tChildren = new List<int>();
        SelectChildren(pattern, pIndex, strictness, pChildren);
        SelectChildren(target, tIndex, strictness, tChildren);

        return MatchSequence(target, tChildren, pattern, pChildren, strictness, env, undo, 0, 0);
    }

    private static void SelectChildren(SyntaxTree tree, int index, Strictness strictness, List<int> into)
    {
        // Smart (default) and Cst keep unnamed tokens. Smart also keeps tree-sitter extras
        // (comments) so $$$ captures match ast-grep's multi array; MatchSequence skips extras
        // when matching non-multi pattern children. Ast/Relaxed drop unnamed + extras up front.
        if (strictness is Strictness.Ast or Strictness.Relaxed)
        {
            foreach (var c in tree.NamedChildren(index))
            {
                if (!tree[c].IsExtra)
                {
                    into.Add(c);
                }
            }

            return;
        }

        if (strictness is Strictness.Cst)
        {
            foreach (var c in tree.Children(index))
            {
                into.Add(c);
            }

            return;
        }

        // Smart: keep extras for multi capture; skip them during ordinary matching.
        foreach (var c in tree.Children(index))
        {
            into.Add(c);
        }
    }

    private static bool MatchSequence(
        SyntaxTree target,
        List<int> tChildren,
        SyntaxTree pattern,
        List<int> pChildren,
        Strictness strictness,
        Dictionary<string, MetaVarValue> env,
        List<EnvUndo> undo,
        int ti,
        int pi
    )
    {
        while (pi < pChildren.Count && ti <= tChildren.Count)
        {
            ref readonly var pNode = ref pattern[pChildren[pi]];
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

                        Bind(env, undo, name, captured);
                    }

                    return true;
                }

                for (var end = ti; end <= tChildren.Count; end++)
                {
                    var mark = undo.Count;
                    if (MatchSequence(target, tChildren, pattern, pChildren, strictness, env, undo, end, pi))
                    {
                        if (name is not ("_" or ""))
                        {
                            var captured = CaptureSlice(target, tChildren, ti, end);
                            if (env.TryGetValue(name, out var existing) && existing.Text != captured.Text)
                            {
                                Revert(env, undo, mark);
                                continue;
                            }

                            Bind(env, undo, name, captured);
                        }

                        return true;
                    }

                    Revert(env, undo, mark);
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

            // smart: skip comments (tree-sitter extras) when matching a non-multi pattern child
            if (strictness == Strictness.Smart && target[tChildren[ti]].IsExtra)
            {
                ti++;
                continue;
            }

            // smart: skip target-only modifiers (final/public/...) before a metavar so
            // `catch ($E $N)` matches `catch (final Exception e)` like ast-grep.
            if (
                strictness == Strictness.Smart
                && pNode.MetaVarName is not null
                && IsSkippableModifier(target, tChildren[ti])
            )
            {
                ti++;
                continue;
            }

            if (!MatchNode(target, tChildren[ti], pattern, pChildren[pi], strictness, env, undo))
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

        // Trailing children on target: Smart skips unnamed + named (ast-grep Smart),
        // so `if ($C) { $$$B }` matches if-else / try-catch-finally. Cst rejects any remainder.
        while (ti < tChildren.Count)
        {
            if (strictness == Strictness.Cst)
            {
                return false;
            }

            if (strictness == Strictness.Smart)
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

    /// <summary>
    /// Capture $$$ as a contiguous source slice plus per-node parts (ast-grep multi array).
    /// </summary>
    private static MetaVarValue CaptureSlice(SyntaxTree target, List<int> tChildren, int from, int to)
    {
        if (from >= to || from >= tChildren.Count)
        {
            // Empty capture: place at the boundary of the neighbor if any.
            var pos =
                from < tChildren.Count ? target[tChildren[from]].Start
                : to > 0 && to - 1 < tChildren.Count ? target[tChildren[to - 1]].End
                : 0;
            return new MetaVarValue(string.Empty, pos, pos, IsMulti: true, Parts: []);
        }

        var parts = new List<MetaVarSpan>(to - from);
        for (var i = from; i < to && i < tChildren.Count; i++)
        {
            ref readonly var n = ref target[tChildren[i]];
            var piece =
                n.Start >= 0 && n.End >= n.Start && n.End <= target.Source.Length
                    ? target.Source[n.Start..n.End]
                    : string.Empty;
            parts.Add(new MetaVarSpan(piece, n.Start, n.End));
        }

        var start = target[tChildren[from]].Start;
        var end = target[tChildren[Math.Min(to, tChildren.Count) - 1]].End;
        var text = start >= 0 && end >= start && end <= target.Source.Length ? target.Source[start..end] : string.Empty;
        return new MetaVarValue(text, start, end, IsMulti: true, Parts: parts);
    }

    private static readonly HashSet<string> SkippableModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "final",
        "public",
        "private",
        "protected",
        "static",
        "abstract",
        "synchronized",
        "native",
        "transient",
        "volatile",
        "default",
        "strictfp",
    };

    private static bool IsSkippableModifier(SyntaxTree target, int index)
    {
        ref readonly var n = ref target[index];
        if (n.IsExtra)
        {
            return true;
        }

        if (!n.IsNamed)
        {
            return false;
        }

        // ANTLR wraps `final` etc. in variableModifier / classOrInterfaceModifier rules.
        // tree-sitter-java uses modifiers / modifier.
        if (
            string.Equals(n.TypeName, "variableModifier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.TypeName, "classOrInterfaceModifier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.TypeName, "modifier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.TypeName, "modifiers", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        if (!n.IsTerminal)
        {
            return false;
        }

        return SkippableModifiers.Contains(target.Text(index));
    }

    /// <summary>
    /// B6 list guard: pattern metavar node has exactly one named child of kind K, but the target
    /// node of the same kind has two or more named children of kind K.
    /// </summary>
    private static bool IsSingleChildListMismatch(SyntaxTree pattern, int pIndex, SyntaxTree target, int tIndex)
    {
        string? childKind = null;
        var pNamedCount = 0;
        foreach (var c in pattern.NamedChildren(pIndex))
        {
            pNamedCount++;
            if (pNamedCount == 1)
            {
                childKind = pattern[c].TypeName;
            }
            else
            {
                return false;
            }
        }

        if (pNamedCount != 1 || string.IsNullOrEmpty(childKind))
        {
            return false;
        }

        // dotted_name / attribute chains legitimately have multiple identifier children
        // (e.g. `ctypes.wintypes` vs pattern `$M` compiled as a single-identifier dotted_name).
        ref readonly var pNode = ref pattern[pIndex];
        var pKind = pNode.TypeName;
        if (
            string.Equals(pKind, "dotted_name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pKind, "attribute", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        // Only apply when the target node itself matches the pattern metavar node's kind (or chain).
        if (
            !string.Equals(pKind, target[tIndex].TypeName, StringComparison.Ordinal)
            && !target.KindMatches(tIndex, pKind)
        )
        {
            return false;
        }

        // Use exact TypeName only. KindMatches would treat chained expression→primary heads as
        // "primary", so binary expressions (two expression children) falsely trip this guard.
        var tCount = 0;
        foreach (var c in target.NamedChildren(tIndex))
        {
            if (string.Equals(target[c].TypeName, childKind, StringComparison.Ordinal))
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

    private static void Bind(Dictionary<string, MetaVarValue> env, List<EnvUndo> undo, string name, MetaVarValue value)
    {
        if (env.TryGetValue(name, out var prev))
        {
            undo.Add(new EnvUndo(name, HadPrevious: true, prev));
        }
        else
        {
            undo.Add(new EnvUndo(name, HadPrevious: false, Previous: null));
        }

        env[name] = value;
    }

    private static void Revert(Dictionary<string, MetaVarValue> env, List<EnvUndo> undo, int mark)
    {
        for (var i = undo.Count - 1; i >= mark; i--)
        {
            var entry = undo[i];
            if (entry.HadPrevious)
            {
                env[entry.Key] = entry.Previous!;
            }
            else
            {
                env.Remove(entry.Key);
            }
        }

        undo.RemoveRange(mark, undo.Count - mark);
    }

    private readonly record struct EnvUndo(string Key, bool HadPrevious, MetaVarValue? Previous);
}
