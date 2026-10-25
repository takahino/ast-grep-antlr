using AstGrepAntlr.Language;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Pattern;

public sealed record PatternSpec(
    string Text,
    string? Entry = null,
    string? Context = null,
    string? Selector = null,
    Strictness Strictness = Strictness.Smart
);

public sealed record CompiledPattern(
    SyntaxTree Tree,
    int Root,
    Strictness Strictness,
    string EntryRule,
    IReadOnlyList<string> RequiredLiterals
);

public sealed class PatternCompiler
{
    private const int MaxVariantAttempts = 4096;

    public CompiledPattern Compile(ILanguage language, PatternSpec spec)
    {
        var text = spec.Text;
        if (!string.IsNullOrEmpty(spec.Context) && !string.IsNullOrEmpty(spec.Selector))
        {
            // Context/selector: parse context, then locate selector subtree by kind name.
            text = spec.Context!;
        }

        var rulesToTry = new List<string>();
        if (!string.IsNullOrEmpty(spec.Entry))
        {
            rulesToTry.Add(spec.Entry!);
        }

        foreach (var r in language.PatternRules)
        {
            if (!rulesToTry.Contains(r, StringComparer.Ordinal))
            {
                rulesToTry.Add(r);
            }
        }

        var occurrences = MetaVar.FindAll(text);
        var variants = language.MetaVarVariants;
        if (variants.Count == 0)
        {
            variants = ["{0}"];
        }

        // Per-occurrence variant choices. Prefer a few seeds (block bodies / empty lists) before
        // the full odometer so patterns with many metavars stay within MaxVariantAttempts.
        var stmtVariant = IndexOfVariant(variants, "{0};");
        var intVariant = IndexOfVariant(variants, "int {0}");
        var seedPlans = BuildSeedPlans(occurrences.Count, occurrences, stmtVariant, intVariant);

        Exception? last = null;
        var attempts = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        CompiledPattern? TryChoices(int[] choices)
        {
            if (attempts >= MaxVariantAttempts)
            {
                return null;
            }

            attempts++;
            var key = string.Join(',', choices.Take(Math.Max(occurrences.Count, 0)));
            if (!seen.Add(key))
            {
                return null;
            }

            var rewritten = MetaVar.ReplaceWithPlaceholders(
                text,
                language.MetaVarPlaceholderTemplate,
                occurrences,
                variants,
                choices,
                out var placeholders,
                out var placeholderSpans
            );

            foreach (var rule in rulesToTry)
            {
                try
                {
                    var tree = language.TryParseFragment(rewritten, rule);
                    if (tree is null)
                    {
                        continue;
                    }

                    var marked = MarkMetaVars(tree, placeholders, placeholderSpans);
                    if (marked < placeholders.Count)
                    {
                        continue;
                    }

                    var root = DescendPatternRoot(tree, tree.Root);
                    if (!string.IsNullOrEmpty(spec.Selector))
                    {
                        root = FindSelector(tree, spec.Selector!) ?? root;
                    }

                    var literals = ExtractRequiredLiterals(tree, placeholders.Keys);
                    return new CompiledPattern(tree, root, spec.Strictness, rule, literals);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            return null;
        }

        foreach (var seed in seedPlans)
        {
            var hit = TryChoices((int[])seed.Clone());
            if (hit is not null)
            {
                return hit;
            }
        }

        var choices = new int[Math.Max(occurrences.Count, 1)];
        do
        {
            var hit = TryChoices(choices);
            if (hit is not null)
            {
                return hit;
            }
        } while (AdvanceChoices(choices, variants.Count, occurrences.Count) && attempts < MaxVariantAttempts);

        var tried = string.Join(", ", rulesToTry);
        throw new InvalidOperationException(
            $"Failed to parse pattern as any of [{tried}]. Try `ag4 debug pattern` or set pattern.entry. {(last is null ? "" : last.Message)}"
        );
    }

    private static int IndexOfVariant(IReadOnlyList<string> variants, string template)
    {
        for (var i = 0; i < variants.Count; i++)
        {
            if (string.Equals(variants[i], template, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<int[]> BuildSeedPlans(
        int slotCount,
        IReadOnlyList<MetaVar.Occurrence> occurrences,
        int stmtVariant,
        int intVariant
    )
    {
        var plans = new List<int[]>();
        void Add(int[] plan) => plans.Add(plan);

        Add(new int[Math.Max(slotCount, 1)]);

        if (stmtVariant >= 0 && occurrences.Count > 0)
        {
            var plan = new int[occurrences.Count];
            for (var i = 0; i < occurrences.Count; i++)
            {
                if (occurrences[i].IsMulti)
                {
                    plan[i] = stmtVariant;
                }
            }

            Add(plan);
        }

        // Mixed: statement form for body-like multis; int {0} for args; "{0}" for inits/updates.
        if (occurrences.Count > 0)
        {
            var plan = new int[occurrences.Count];
            for (var i = 0; i < occurrences.Count; i++)
            {
                if (!occurrences[i].IsMulti)
                {
                    continue;
                }

                var name = occurrences[i].Name;
                if (
                    stmtVariant >= 0
                    && (
                        name.Contains("BODY", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("TRY", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("HANDLER", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("CATCH", StringComparison.OrdinalIgnoreCase)
                        || name is "B" or "T"
                    )
                )
                {
                    plan[i] = stmtVariant;
                }
                else if (
                    intVariant >= 0
                    && (
                        name.Contains("ARG", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("PARAM", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    plan[i] = intVariant;
                }
            }

            Add(plan);
        }

        return plans;
    }

    /// <summary>Advance odometer; prefer changing the rightmost metavar first (nested bodies).</summary>
    private static bool AdvanceChoices(int[] choices, int variantCount, int occurrenceCount)
    {
        if (occurrenceCount == 0)
        {
            return false;
        }

        for (var i = occurrenceCount - 1; i >= 0; i--)
        {
            choices[i]++;
            if (choices[i] < variantCount)
            {
                return true;
            }

            choices[i] = 0;
        }

        return false;
    }

    private static int DescendPatternRoot(SyntaxTree tree, int root)
    {
        while (true)
        {
            var n = tree[root];
            // Stop on meta-variables and non-rule nodes (plan B2).
            if (n.MetaVarName is not null || n.Kind != SyntaxNodeKind.Rule)
            {
                break;
            }

            var children = tree.Children(root)
                .Where(c =>
                {
                    if (tree[c].Kind != SyntaxNodeKind.Token)
                    {
                        return true;
                    }

                    var t = tree.Text(c);
                    return !string.Equals(t, "<EOF>", StringComparison.Ordinal);
                })
                .ToList();
            if (children.Count != 1)
            {
                break;
            }

            root = children[0];
        }

        return root;
    }

    private static int MarkMetaVars(
        SyntaxTree tree,
        IReadOnlyDictionary<string, MetaVar.Occurrence> placeholders,
        IReadOnlyDictionary<string, (int Start, int End)> placeholderSpans
    )
    {
        var marked = 0;
        foreach (var (placeholder, occ) in placeholders)
        {
            if (!placeholderSpans.TryGetValue(placeholder, out var span))
            {
                continue;
            }

            // Empty insertions cannot be marked; reject this variant combination.
            if (span.End <= span.Start && occ.IsMulti)
            {
                continue;
            }

            var index = FindNodeByExactSpan(tree, span.Start, span.End);
            if (index < 0)
            {
                index = FindMetaVarNode(tree, placeholder, span.Start);
            }

            if (index < 0)
            {
                continue;
            }

            // Prefer the outermost node whose span equals the inserted placeholder text span.
            var mark = index;
            var parent = tree[mark].Parent;
            while (parent >= 0 && tree[parent].Start == span.Start && tree[parent].End == span.End)
            {
                mark = parent;
                parent = tree[parent].Parent;
            }

            // If we only found the bare identifier token, walk single-child rule chains so typed
            // metavars like ${A:expression} land on the expression chain head.
            if (tree[mark].Kind == SyntaxNodeKind.Token || tree[mark].ChildCount == 1)
            {
                parent = tree[mark].Parent;
                while (parent >= 0 && tree[parent].ChildCount == 1)
                {
                    mark = parent;
                    parent = tree[parent].Parent;
                }
            }

            tree.SetMetaVar(mark, occ.Name, occ.IsMulti, occ.TypedRule);
            marked++;
        }

        return marked;
    }

    private static int FindNodeByExactSpan(SyntaxTree tree, int start, int end)
    {
        var best = -1;
        for (var i = 0; i < tree.NodeCount; i++)
        {
            if (tree[i].Start != start || tree[i].End != end)
            {
                continue;
            }

            // Prefer rule nodes over tokens when spans match.
            if (best < 0 || tree[i].Kind == SyntaxNodeKind.Rule)
            {
                best = i;
            }
        }

        return best;
    }

    private static int FindMetaVarNode(SyntaxTree tree, string placeholder, int approximateStart)
    {
        var best = -1;
        var bestDist = int.MaxValue;
        for (var i = 0; i < tree.NodeCount; i++)
        {
            if (tree[i].Kind != SyntaxNodeKind.Token)
            {
                continue;
            }

            var text = tree.Text(i);
            if (!TextContainsPlaceholder(text, placeholder))
            {
                continue;
            }

            var dist = approximateStart >= 0 ? Math.Abs(tree[i].Start - approximateStart) : 0;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Match bare placeholders and path/string tokens that wrap them
    /// (e.g. <c>&lt;__MV0__&gt;</c>, <c>"__MV0__"</c> for #include patterns).
    /// </summary>
    private static bool TextContainsPlaceholder(string text, string placeholder)
    {
        if (string.Equals(text, placeholder, StringComparison.Ordinal))
        {
            return true;
        }

        if (text.Length >= placeholder.Length + 2)
        {
            if (
                text[0] == '<'
                && text[^1] == '>'
                && text.AsSpan(1, text.Length - 2).SequenceEqual(placeholder.AsSpan())
            )
            {
                return true;
            }

            if ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''))
            {
                return text.AsSpan(1, text.Length - 2).SequenceEqual(placeholder.AsSpan());
            }
        }

        return false;
    }

    private static int? FindSelector(SyntaxTree tree, string selectorKind)
    {
        for (var i = 0; i < tree.NodeCount; i++)
        {
            if (tree.KindMatches(i, selectorKind))
            {
                return i;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractRequiredLiterals(SyntaxTree tree, IEnumerable<string> placeholderTexts)
    {
        var placeholders = new HashSet<string>(placeholderTexts, StringComparer.Ordinal);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tree.NodeCount; i++)
        {
            if (tree[i].Kind != SyntaxNodeKind.Token || !tree[i].IsNamed)
            {
                continue;
            }

            var t = tree.Text(i);
            if (string.IsNullOrEmpty(t) || placeholders.Contains(t))
            {
                continue;
            }

            if (t.Length >= 2)
            {
                set.Add(t);
            }
        }

        return set.ToList();
    }
}
