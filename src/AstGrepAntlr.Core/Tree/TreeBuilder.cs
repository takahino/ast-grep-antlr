using System.Runtime.CompilerServices;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AstGrepAntlr.Language;

namespace AstGrepAntlr.Tree;

/// <summary>Converts an ANTLR <see cref="IParseTree"/> into an arena <see cref="SyntaxTree"/> with chain collapse.</summary>
public sealed class TreeBuilder
{
    private static readonly ConditionalWeakTable<IAntlrLanguage, TokenTypeInfo> TokenInfoCache = new();

    public SyntaxTree Build(IAntlrLanguage language, string source, IParseTree root)
    {
        var asm = new SyntaxTreeAssembler();
        // ANTLR CharStreams.fromString reports code-point offsets; .NET strings are UTF-16.
        var cpToUtf16 = BuildCodePointMap(source);
        var tokenInfo = TokenInfoCache.GetValue(language, static l => new TokenTypeInfo(l));

        BuildNode(language, tokenInfo, root, parent: -1, asm, cpToUtf16);

        return asm.Build(source, language.Id);
    }

    /// <summary>
    /// Per-language lookup of token type name and <see cref="IAntlrLanguage.IsNamedToken"/>, indexed
    /// by token type. Every builtin and interpreted language decides named-ness from the token type
    /// alone, so caching per type skips the vocabulary lookups and string trimming per token.
    /// </summary>
    private sealed class TokenTypeInfo
    {
        private readonly IAntlrLanguage _language;
        private readonly string?[] _names;
        private readonly bool[] _named;

        public TokenTypeInfo(IAntlrLanguage language)
        {
            _language = language;
            // Slot 0 holds EOF (-1), so slot = type + 1.
            var size = Math.Max(0, language.MaxTokenType) + 2;
            _names = new string?[size];
            _named = new bool[size];
            for (var type = -1; type < size - 1; type++)
            {
                _names[type + 1] =
                    language.Vocabulary.GetSymbolicName(type) ?? language.Vocabulary.GetDisplayName(type);
                _named[type + 1] = language.IsNamedToken(new CommonToken(type), language.Vocabulary);
            }
        }

        public string Name(IToken t)
        {
            var slot = t.Type + 1;
            return (uint)slot < (uint)_names.Length && _names[slot] is { } name
                ? name
                : _language.Vocabulary.GetSymbolicName(t.Type)
                    ?? _language.Vocabulary.GetDisplayName(t.Type)
                    ?? t.Text
                    ?? "TOKEN";
        }

        public bool IsNamed(IToken t)
        {
            var slot = t.Type + 1;
            return (uint)slot < (uint)_named.Length ? _named[slot] : _language.IsNamedToken(t, _language.Vocabulary);
        }
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
        TokenTypeInfo tokenInfo,
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
            typeName = tokenInfo.Name(t);
            start = ToUtf16(Math.Max(0, t.StartIndex), cpToUtf16, utf16Len);
            end = ToUtf16(t.StopIndex >= t.StartIndex ? t.StopIndex + 1 : t.StartIndex, cpToUtf16, utf16Len);
            isNamed = tokenInfo.IsNamed(t);
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
            var childIndex = BuildNode(language, tokenInfo, childTree, index, asm, cpToUtf16);
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
