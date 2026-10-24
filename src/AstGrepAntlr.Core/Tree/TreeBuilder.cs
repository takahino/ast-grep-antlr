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
        var chainKinds = new List<string[]>();
        // ANTLR CharStreams.fromString reports code-point offsets; .NET strings are UTF-16.
        var cpToUtf16 = BuildCodePointMap(source);

        BuildNode(language, root, parent: -1, nodes, kindIndex, chainKinds, cpToUtf16);

        // Second pass: collapse single-child rule chains and set ChainHead / ChainKinds.
        CollapseChains(nodes, chainKinds);

        return new SyntaxTree(source, language.Id, nodes.ToArray(), kindIndex, chainKinds.ToArray());
    }

    private static int[]? BuildCodePointMap(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (char.IsSurrogate(source[i]))
            {
                var map = new List<int>(source.Length + 1);
                for (var j = 0; j < source.Length; )
                {
                    map.Add(j);
                    if (char.IsHighSurrogate(source[j]) && j + 1 < source.Length && char.IsLowSurrogate(source[j + 1]))
                    {
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                map.Add(source.Length);
                return map.ToArray();
            }
        }

        return null;
    }

    private static int ToUtf16(int codePointOffset, int[]? map, int utf16Length)
    {
        if (map is null)
        {
            return Math.Clamp(codePointOffset, 0, utf16Length);
        }

        if (codePointOffset <= 0)
        {
            return 0;
        }

        if (codePointOffset >= map.Length - 1)
        {
            return utf16Length;
        }

        return map[codePointOffset];
    }

    private static int BuildNode(
        ILanguage language,
        IParseTree tree,
        int parent,
        List<SyntaxNode> nodes,
        Dictionary<string, List<int>> kindIndex,
        List<string[]> chainKinds,
        int[]? cpToUtf16
    )
    {
        var index = nodes.Count;
        nodes.Add(default); // placeholder
        chainKinds.Add([]);

        SyntaxNodeKind kind;
        int typeId;
        string typeName;
        int start;
        int end;
        bool isNamed;

        // Source length for clamping is unknown here; use int.MaxValue then clamp via map helper.
        // Actual source length is recovered from the map sentinel or left unbounded for BMP.
        var utf16Len = cpToUtf16 is null ? int.MaxValue : cpToUtf16[^1];

        if (tree is IErrorNode errorNode)
        {
            var t = errorNode.Symbol;
            kind = t.Type == TokenConstants.InvalidType ? SyntaxNodeKind.Missing : SyntaxNodeKind.Error;
            typeId = t.Type;
            typeName = kind == SyntaxNodeKind.Missing ? "MISSING" : "ERROR";
            start = ToUtf16(Math.Max(0, t.StartIndex), cpToUtf16, utf16Len);
            end = ToUtf16(Math.Max(t.StartIndex, t.StopIndex + 1), cpToUtf16, utf16Len);
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
            start = ToUtf16(Math.Max(0, t.StartIndex), cpToUtf16, utf16Len);
            end = ToUtf16(t.StopIndex >= t.StartIndex ? t.StopIndex + 1 : t.StartIndex, cpToUtf16, utf16Len);
            isNamed = language.IsNamedToken(t, language.Vocabulary);
        }
        else if (tree is ParserRuleContext rule)
        {
            kind = SyntaxNodeKind.Rule;
            typeId = rule.RuleIndex;
            typeName = language.RuleNames[rule.RuleIndex];
            start = ToUtf16(rule.Start?.StartIndex ?? 0, cpToUtf16, utf16Len);
            end = ToUtf16(
                rule.Stop != null ? rule.Stop.StopIndex + 1 : rule.Start?.StartIndex ?? 0,
                cpToUtf16,
                utf16Len
            );
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
            var childIndex = BuildNode(language, childTree, index, nodes, kindIndex, chainKinds, cpToUtf16);
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

    private static void CollapseChains(List<SyntaxNode> nodes, List<string[]> chainKinds)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Kind != SyntaxNodeKind.Rule || nodes[i].ChainHead != i)
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
            // Stored pre-split: KindMatches runs over every node of every tree, so splitting a
            // joined string there would allocate on the hottest path in the matcher.
            chainKinds[i] = parts.ToArray();
        }
    }
}
