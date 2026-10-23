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
    private readonly TwoStageParser _parser = new();

    public CompiledPattern Compile(ILanguage language, PatternSpec spec)
    {
        var text = spec.Text;
        if (!string.IsNullOrEmpty(spec.Context) && !string.IsNullOrEmpty(spec.Selector))
        {
            // Context/selector: parse context, then locate selector subtree by kind name.
            text = spec.Context!;
        }

        var rewritten = MetaVar.ReplaceWithPlaceholders(
            text,
            language.MetaVarPlaceholderTemplate,
            out var placeholders
        );
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

        Exception? last = null;
        foreach (var rule in rulesToTry)
        {
            try
            {
                var result = ParsePatternFragment(language, rewritten, rule);
                if (result is null)
                {
                    continue;
                }

                MarkMetaVars(result.Tree, placeholders);
                var root = result.Tree.Root;
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

        var tried = string.Join(", ", rulesToTry);
        throw new InvalidOperationException(
            $"Failed to parse pattern as any of [{tried}]. Try `ag4 debug pattern` or set pattern.entry. {(last is null ? "" : last.Message)}"
        );
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

    private static void MarkMetaVars(SyntaxTree tree, IReadOnlyDictionary<string, MetaVar.Occurrence> placeholders)
    {
        for (var i = 0; i < tree.NodeCount; i++)
        {
            if (tree[i].Kind != SyntaxNodeKind.Token)
            {
                continue;
            }

            var text = tree.Text(i);
            if (!placeholders.TryGetValue(text, out var occ))
            {
                continue;
            }

            // Walk up single-child parents to the first meaningful pattern node.
            var mark = i;
            var parent = tree[i].Parent;
            while (parent >= 0 && tree[parent].ChildCount == 1)
            {
                mark = parent;
                parent = tree[parent].Parent;
            }

            tree.SetMetaVar(mark, occ.Name, occ.IsMulti);
        }
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
