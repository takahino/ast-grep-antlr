using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AstGrepAntlr.Language;

namespace AstGrepAntlr.Tree;

/// <summary>Converts an ANTLR <see cref="IParseTree"/> into an arena <see cref="SyntaxTree"/> with chain collapse.</summary>
public sealed class TreeBuilder
{
    public SyntaxTree Build(IAntlrLanguage language, string source, IParseTree root)
    {
        var asm = new SyntaxTreeAssembler();
        // ANTLR CharStreams.fromString reports code-point offsets; .NET strings are UTF-16.
        var cpToUtf16 = BuildCodePointMap(source);

        BuildNode(language, root, parent: -1, asm, cpToUtf16);

        return asm.Build(source, language.Id);
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
        IAntlrLanguage language,
        IParseTree tree,
        int parent,
        SyntaxTreeAssembler asm,
        int[]? cpToUtf16
    )
    {
        var index = asm.Begin();

        SyntaxNodeKind kind;
        int typeId;
        string typeName;
        int start;
        int end;
        bool isNamed;

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
            var childIndex = BuildNode(language, childTree, index, asm, cpToUtf16);
            childCount++;
            if (firstChild < 0)
            {
                firstChild = childIndex;
            }
            else
            {
                asm.SetNextSibling(prevChild, childIndex);
            }

            prevChild = childIndex;
        }

        asm.Commit(
            index,
            new SyntaxNode
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
            }
        );

        return index;
    }
}
