using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using AstGrepAntlr.Language;
using AstGrepAntlr.Parsing;
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
    private const int MaxVariantAttempts = 256;

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

        // Per-occurrence variant choices (odometer). Same template for all is tried first.
        var choices = new int[Math.Max(occurrences.Count, 1)];
        Exception? last = null;
        var attempts = 0;

        do
        {
            if (attempts++ >= MaxVariantAttempts)
            {
                break;
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
                    var result = ParsePatternFragment(language, rewritten, rule);
                    if (result is null)
                    {
                        continue;
                    }

                    MarkMetaVars(result.Tree, placeholders, placeholderSpans);
                    var root = DescendPatternRoot(result.Tree, result.Tree.Root);
                    if (!string.IsNullOrEmpty(spec.Selector))
                    {
                        root = FindSelector(result.Tree, spec.Selector!) ?? root;
                    }

                    var literals = ExtractRequiredLiterals(result.Tree, placeholders.Keys);
                    return new CompiledPattern(result.Tree, root, spec.Strictness, rule, literals);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
        } while (AdvanceChoices(choices, variants.Count, occurrences.Count));

        var tried = string.Join(", ", rulesToTry);
        throw new InvalidOperationException(
            $"Failed to parse pattern as any of [{tried}]. Try `ag4 debug pattern` or set pattern.entry. {(last is null ? "" : last.Message)}"
        );
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

    private static ParseResult? ParsePatternFragment(ILanguage language, string source, string rule)
    {
        var stream = CharStreams.fromString(source);
        var lexer = language.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        parser.ErrorHandler = new BailErrorStrategy();
        parser.Interpreter.PredictionMode = PredictionMode.SLL;
        try
        {
            var tree = language.InvokeRule(parser, rule);
            if (tokens.LA(1) != TokenConstants.EOF)
            {
                return null;
            }

            var builder = new TreeBuilder();
            var syntax = builder.Build(language, source, tree);
            return new ParseResult(syntax, Array.Empty<ParseDiagnostic>(), rule, false);
        }
        catch (ParseCanceledException)
        {
            return null;
        }
        catch (RecognitionException)
        {
            return null;
        }
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

    private static void MarkMetaVars(
        SyntaxTree tree,
        IReadOnlyDictionary<string, MetaVar.Occurrence> placeholders,
        IReadOnlyDictionary<string, (int Start, int End)> placeholderSpans
    )
    {
        foreach (var (placeholder, occ) in placeholders)
        {
            if (!placeholderSpans.TryGetValue(placeholder, out var span))
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
        }
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

            if (!string.Equals(tree.Text(i), placeholder, StringComparison.Ordinal))
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
