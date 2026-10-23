using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AstGrepAntlr.Language;

namespace AstGrepAntlr.Tree;

/// <summary>Converts an ANTLR <see cref="IParseTree"/> into an arena <see cref="SyntaxTree"/> with chain collapse.</summary>
public sealed class TreeBuilder
{
    public SyntaxTree Build(ILanguage language, string source, IParseTree root)
    {
        var nodes = new List<SyntaxNode>();
        var kindIndex = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var chainKinds = new List<string>();

        BuildNode(language, root, parent: -1, nodes, kindIndex, chainKinds);

        // Second pass: collapse single-child rule chains and set ChainHead / ChainKinds.
        CollapseChains(nodes, chainKinds);

        return new SyntaxTree(source, language.Id, nodes.ToArray(), kindIndex, chainKinds.ToArray());
    }

    private static int BuildNode(
        ILanguage language,
        IParseTree tree,
        int parent,
        List<SyntaxNode> nodes,
        Dictionary<string, List<int>> kindIndex,
        List<string> chainKinds
    )
    {
        var index = nodes.Count;
        nodes.Add(default); // placeholder
        chainKinds.Add(string.Empty);

        SyntaxNodeKind kind;
        int typeId;
        string typeName;
        int start;
        int end;
        bool isNamed;
        string? fieldName = null;

        if (tree is IErrorNode errorNode)
        {
            var t = errorNode.Symbol;
            kind = t.Type == TokenConstants.InvalidType ? SyntaxNodeKind.Missing : SyntaxNodeKind.Error;
            typeId = t.Type;
            typeName = kind == SyntaxNodeKind.Missing ? "MISSING" : "ERROR";
            start = Math.Max(0, t.StartIndex);
            end = Math.Max(start, t.StopIndex + 1);
            isNamed = true;
        }
        else if (tree is ITerminalNode terminal)
        {
            var t = terminal.Symbol;
            kind = SyntaxNodeKind.Token;
            typeId = t.Type;
            typeName =
                language.Vocabulary.GetSymbolicName(t.Type)
                ?? language.Vocabulary.GetDisplayName(t.Type)
                ?? t.Text
                ?? "TOKEN";
            start = Math.Max(0, t.StartIndex);
            end = t.StopIndex >= t.StartIndex ? t.StopIndex + 1 : start;
            isNamed = language.IsNamedToken(t, language.Vocabulary);
        }
        else if (tree is ParserRuleContext rule)
        {
            kind = SyntaxNodeKind.Rule;
            typeId = rule.RuleIndex;
            typeName = language.RuleNames[rule.RuleIndex];
            start = rule.Start?.StartIndex ?? 0;
            end = rule.Stop != null ? rule.Stop.StopIndex + 1 : start;
            isNamed = true;
        }
        else
        {
            kind = SyntaxNodeKind.Rule;
            typeId = -1;
            typeName = tree.GetType().Name;
            start = 0;
            end = 0;
            isNamed = true;
        }

        var firstChild = -1;
        var prevChild = -1;
        var childCount = 0;
        for (var i = 0; i < tree.ChildCount; i++)
        {
            var childTree = tree.GetChild(i);
            var childIndex = BuildNode(language, childTree, index, nodes, kindIndex, chainKinds);
            childCount++;
            if (firstChild < 0)
            {
                firstChild = childIndex;
            }
            else
            {
                var prev = nodes[prevChild];
                nodes[prevChild] = prev with { NextSibling = childIndex };
            }

            prevChild = childIndex;
        }

        nodes[index] = new SyntaxNode
        {
            Index = index,
            Kind = kind,
            TypeId = typeId,
            TypeName = typeName,
            Start = start,
            End = Math.Max(start, end),
            Parent = parent,
            FirstChild = firstChild,
            NextSibling = -1,
            ChildCount = childCount,
            IsNamed = isNamed,
            ChainHead = index,
            FieldName = fieldName,
        };

        AddKind(kindIndex, typeName, index);
        if (kind is SyntaxNodeKind.Error or SyntaxNodeKind.Missing)
        {
            AddKind(kindIndex, "ERROR", index);
        }

        return index;
    }

    private static void AddKind(Dictionary<string, List<int>> kindIndex, string kind, int index)
    {
        if (!kindIndex.TryGetValue(kind, out var list))
        {
            list = [];
            kindIndex[kind] = list;
        }

        list.Add(index);
    }

    private static void CollapseChains(List<SyntaxNode> nodes, List<string> chainKinds)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Kind != SyntaxNodeKind.Rule)
            {
                continue;
            }

            // Find longest single-child rule chain starting at i.
            var parts = new List<string> { nodes[i].TypeName };
            var current = i;
            while (nodes[current].ChildCount == 1 && nodes[current].FirstChild >= 0)
            {
                var child = nodes[current].FirstChild;
                if (nodes[child].Kind != SyntaxNodeKind.Rule)
                {
                    break;
                }

                parts.Add(nodes[child].TypeName);
                // Point child chain head to i
                nodes[child] = nodes[child] with
                {
                    ChainHead = i,
                };
                current = child;
            }

            nodes[i] = nodes[i] with { ChainHead = i };
            chainKinds[i] = string.Join('>', parts);

            // Index all chain kinds at the head for kind lookup.
            // (Kind index already has each individually; KindMatches uses chainKinds.)
        }
    }
}
