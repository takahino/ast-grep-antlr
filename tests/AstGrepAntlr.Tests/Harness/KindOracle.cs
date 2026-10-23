using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AstGrepAntlr.Language;
using AstGrepAntlr.Parsing;

namespace AstGrepAntlr.Tests.Harness;

/// <summary>Collects rule names reachable via single-child chains from an ANTLR parse tree.</summary>
public static class KindOracle
{
    public static HashSet<string> ChainKindsAt(ILanguage language, string source, int charStart, int charEnd)
    {
        var tree = new TwoStageParser().Parse(language, source).Tree;
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tree.NodeCount; i++)
        {
            var n = tree[i];
            if (n.Start != charStart || n.End != charEnd)
            {
                continue;
            }

            // Walk ChainHead kinds via KindMatches against every rule name is expensive;
            // instead collect TypeName of this node and its single-child descendants.
            CollectChain(tree, i, result);
        }

        return result;
    }

    public static Dictionary<int, HashSet<string>> AllNodeKinds(ILanguage language, string source)
    {
        var parsed = new TwoStageParser().Parse(language, source);
        var antlrRoot = Invoke(language, source);
        var fromAntlr = new Dictionary<(int Start, int End), HashSet<string>>();
        Walk(language, antlrRoot, fromAntlr);

        var map = new Dictionary<int, HashSet<string>>();
        var tree = parsed.Tree;
        for (var i = 0; i < tree.NodeCount; i++)
        {
            var n = tree[i];
            var key = (n.Start, n.End);
            if (fromAntlr.TryGetValue(key, out var kinds))
            {
                map[i] = kinds;
            }
            else
            {
                map[i] = new HashSet<string>(StringComparer.Ordinal) { n.TypeName };
            }
        }

        return map;
    }

    private static void CollectChain(AstGrepAntlr.Tree.SyntaxTree tree, int index, HashSet<string> into)
    {
        into.Add(tree[index].TypeName);
        var current = index;
        while (tree[current].ChildCount == 1 && tree[current].FirstChild >= 0)
        {
            var child = tree[current].FirstChild;
            if (tree[child].Kind != AstGrepAntlr.Tree.SyntaxNodeKind.Rule)
            {
                break;
            }

            into.Add(tree[child].TypeName);
            current = child;
        }
    }

    private static ParserRuleContext Invoke(ILanguage language, string source)
    {
        var stream = CharStreams.fromString(source);
        var lexer = language.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        return language.InvokeRule(parser, language.StartRule);
    }

    private static void Walk(ILanguage language, IParseTree node, Dictionary<(int, int), HashSet<string>> into)
    {
        if (node is ParserRuleContext rule && rule.Start is not null)
        {
            var start = rule.Start.StartIndex;
            var end = rule.Stop is not null ? rule.Stop.StopIndex + 1 : start;
            var name = language.RuleNames[rule.RuleIndex];
            var key = (start, end);
            if (!into.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                into[key] = set;
            }

            set.Add(name);

            // Propagate single-child chain names upward by also recording on ancestors later.
            if (rule.ChildCount == 1 && rule.GetChild(0) is ParserRuleContext)
            {
                // child walk will add its own; chain equivalence is checked via KindMatches
            }
        }

        for (var i = 0; i < node.ChildCount; i++)
        {
            Walk(language, node.GetChild(i), into);
        }
    }
}
