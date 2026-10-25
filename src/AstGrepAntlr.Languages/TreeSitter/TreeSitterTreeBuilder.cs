using System.Runtime.InteropServices;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Languages.TreeSitter;

/// <summary>Converts a tree-sitter CST into an arena <see cref="SyntaxTree"/>.</summary>
internal sealed class TreeSitterTreeBuilder
{
    private readonly string[] _symbolNames;
    private readonly Dictionary<ushort, string[]> _supertypeAliases;

    public TreeSitterTreeBuilder(nint language)
    {
        var count = TsNative.ts_language_symbol_count(language);
        _symbolNames = new string[count];
        for (ushort i = 0; i < count; i++)
        {
            _symbolNames[i] = TsNative.MarshalAnsi(TsNative.ts_language_symbol_name(language, i)) ?? $"sym_{i}";
        }

        _supertypeAliases = BuildSupertypeAliases(language);
    }

    public IReadOnlyList<string> RuleNames =>
        _symbolNames.Where(n => !string.IsNullOrEmpty(n) && n is not ("ERROR" or "end")).Distinct().ToList();

    public SyntaxTree Build(string source, string languageId, TsNative.TSNode root)
    {
        var asm = new SyntaxTreeAssembler(collapseChains: false);
        var cursor = TsNative.ts_tree_cursor_new(root);
        try
        {
            BuildNode(ref cursor, parent: -1, asm);
        }
        finally
        {
            TsNative.ts_tree_cursor_delete(ref cursor);
        }

        return asm.Build(source, languageId);
    }

    /// <summary>
    /// Build a fragment tree whose source is <paramref name="fragment"/>, shifting spans so the
    /// embedded range <paramref name="embedStartUtf16"/>..<paramref name="embedEndUtf16"/> maps to 0..fragment.Length.
    /// </summary>
    public SyntaxTree BuildShifted(
        string fragment,
        string languageId,
        TsNative.TSNode node,
        int embedStartUtf16,
        int embedEndUtf16
    )
    {
        var asm = new SyntaxTreeAssembler(collapseChains: false);
        var cursor = TsNative.ts_tree_cursor_new(node);
        try
        {
            BuildNodeShifted(ref cursor, parent: -1, asm, embedStartUtf16, embedEndUtf16, fragment.Length);
        }
        finally
        {
            TsNative.ts_tree_cursor_delete(ref cursor);
        }

        return asm.Build(fragment, languageId);
    }

    private int BuildNode(ref TsNative.TSTreeCursor cursor, int parent, SyntaxTreeAssembler asm)
    {
        var node = TsNative.ts_tree_cursor_current_node(ref cursor);
        var index = asm.Begin();
        var symbol = TsNative.ts_node_symbol(node);
        var typeName = NameOf(symbol);
        var isMissing = TsNative.ts_node_is_missing(node);
        var isError = TsNative.ts_node_is_error(node) || typeName == "ERROR";
        var isExtra = TsNative.ts_node_is_extra(node);
        var hasChildren = TsNative.ts_tree_cursor_goto_first_child(ref cursor);

        SyntaxNodeKind kind;
        if (isMissing)
        {
            kind = SyntaxNodeKind.Missing;
            typeName = "MISSING";
        }
        else if (isError)
        {
            kind = SyntaxNodeKind.Error;
            typeName = "ERROR";
        }
        else if (!hasChildren)
        {
            kind = SyntaxNodeKind.Token;
        }
        else
        {
            kind = SyntaxNodeKind.Rule;
        }

        var start = ByteToUtf16(TsNative.ts_node_start_byte(node));
        var end = ByteToUtf16(TsNative.ts_node_end_byte(node));

        var firstChild = -1;
        var prevChild = -1;
        var childCount = 0;
        if (hasChildren)
        {
            do
            {
                var childIndex = BuildNode(ref cursor, index, asm);
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
            } while (TsNative.ts_tree_cursor_goto_next_sibling(ref cursor));

            TsNative.ts_tree_cursor_goto_parent(ref cursor);
        }

        IReadOnlyList<string>? aliases = null;
        if (_supertypeAliases.TryGetValue(symbol, out var supers))
        {
            aliases = supers;
        }

        asm.Commit(
            index,
            new SyntaxNode
            {
                Index = index,
                Kind = kind,
                TypeId = symbol,
                TypeName = typeName,
                Start = start,
                End = Math.Max(start, end),
                Parent = parent,
                FirstChild = firstChild,
                NextSibling = -1,
                ChildCount = childCount,
                IsNamed = isMissing || isError || TsNative.ts_node_is_named(node),
                ChainHead = index,
                IsExtra = isExtra,
            },
            aliases
        );
        return index;
    }

    private int BuildNodeShifted(
        ref TsNative.TSTreeCursor cursor,
        int parent,
        SyntaxTreeAssembler asm,
        int embedStart,
        int embedEnd,
        int fragmentLength
    )
    {
        var node = TsNative.ts_tree_cursor_current_node(ref cursor);
        var index = asm.Begin();
        var symbol = TsNative.ts_node_symbol(node);
        var typeName = NameOf(symbol);
        var isMissing = TsNative.ts_node_is_missing(node);
        var isError = TsNative.ts_node_is_error(node) || typeName == "ERROR";
        var isExtra = TsNative.ts_node_is_extra(node);
        var hasChildren = TsNative.ts_tree_cursor_goto_first_child(ref cursor);

        SyntaxNodeKind kind;
        if (isMissing)
        {
            kind = SyntaxNodeKind.Missing;
            typeName = "MISSING";
        }
        else if (isError)
        {
            kind = SyntaxNodeKind.Error;
            typeName = "ERROR";
        }
        else if (!hasChildren)
        {
            kind = SyntaxNodeKind.Token;
        }
        else
        {
            kind = SyntaxNodeKind.Rule;
        }

        var rawStart = ByteToUtf16(TsNative.ts_node_start_byte(node));
        var rawEnd = ByteToUtf16(TsNative.ts_node_end_byte(node));
        var start = Math.Clamp(rawStart - embedStart, 0, fragmentLength);
        var end = Math.Clamp(rawEnd - embedStart, 0, fragmentLength);

        var firstChild = -1;
        var prevChild = -1;
        var childCount = 0;
        if (hasChildren)
        {
            do
            {
                var childIndex = BuildNodeShifted(ref cursor, index, asm, embedStart, embedEnd, fragmentLength);
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
            } while (TsNative.ts_tree_cursor_goto_next_sibling(ref cursor));

            TsNative.ts_tree_cursor_goto_parent(ref cursor);
        }

        IReadOnlyList<string>? aliases = null;
        if (_supertypeAliases.TryGetValue(symbol, out var supers))
        {
            aliases = supers;
        }

        asm.Commit(
            index,
            new SyntaxNode
            {
                Index = index,
                Kind = kind,
                TypeId = symbol,
                TypeName = typeName,
                Start = start,
                End = Math.Max(start, end),
                Parent = parent,
                FirstChild = firstChild,
                NextSibling = -1,
                ChildCount = childCount,
                IsNamed = isMissing || isError || TsNative.ts_node_is_named(node),
                ChainHead = index,
                IsExtra = isExtra,
            },
            aliases
        );
        return index;
    }

    private string NameOf(ushort symbol) => symbol < _symbolNames.Length ? _symbolNames[symbol] : $"sym_{symbol}";

    private static int ByteToUtf16(uint byteOffset) =>
        byteOffset > int.MaxValue / 2 ? int.MaxValue : (int)(byteOffset / 2);

    private static Dictionary<ushort, string[]> BuildSupertypeAliases(nint language)
    {
        var map = new Dictionary<ushort, List<string>>();
        var ptr = TsNative.ts_language_supertypes(language, out var length);
        if (ptr == nint.Zero || length == 0)
        {
            return new Dictionary<ushort, string[]>();
        }

        for (uint i = 0; i < length; i++)
        {
            var super = (ushort)Marshal.ReadInt16(ptr, (int)(i * sizeof(ushort)));
            var superName = TsNative.MarshalAnsi(TsNative.ts_language_symbol_name(language, super));
            if (string.IsNullOrEmpty(superName))
            {
                continue;
            }

            var subPtr = TsNative.ts_language_subtypes(language, super, out var subLen);
            if (subPtr == nint.Zero || subLen == 0)
            {
                continue;
            }

            for (uint j = 0; j < subLen; j++)
            {
                var sub = (ushort)Marshal.ReadInt16(subPtr, (int)(j * sizeof(ushort)));
                if (!map.TryGetValue(sub, out var list))
                {
                    list = [];
                    map[sub] = list;
                }

                if (!list.Contains(superName, StringComparer.Ordinal))
                {
                    list.Add(superName);
                }
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
}
