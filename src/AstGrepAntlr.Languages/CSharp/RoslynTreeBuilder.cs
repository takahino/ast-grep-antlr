using AstGrepAntlr.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ArenaNode = AstGrepAntlr.Tree.SyntaxNode;
using ArenaTree = AstGrepAntlr.Tree.SyntaxTree;
using RoslynNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace AstGrepAntlr.Languages.CSharp;

/// <summary>Converts a Roslyn syntax node into an arena <see cref="ArenaTree"/>.</summary>
internal sealed class RoslynTreeBuilder
{
    public ArenaTree Build(string source, string languageId, RoslynNode root)
    {
        var asm = new SyntaxTreeAssembler();
        BuildSyntaxNode(root, parent: -1, asm);
        return asm.Build(source, languageId);
    }

    private static int BuildSyntaxNode(RoslynNode node, int parent, SyntaxTreeAssembler asm)
    {
        var index = asm.Begin();
        var kind = node.Kind();
        var typeName = RoslynKindNames.ToSnakeCase(kind);
        IReadOnlyList<string>? aliases;
        if (kind == SyntaxKind.NumericLiteralExpression && node is LiteralExpressionSyntax lit)
        {
            aliases = RoslynKindNames.LooksLikeRealLiteral(lit.Token.Text) ? ["real_literal"] : ["integer_literal"];
        }
        else
        {
            aliases = RoslynKindNames.Aliases(kind);
        }

        var firstChild = -1;
        var prevChild = -1;
        var childCount = 0;

        // Leading skipped tokens (error recovery) as Error children before real children.
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SkippedTokensTrivia))
            {
                continue;
            }

            foreach (var skipped in trivia.GetStructure()!.ChildTokens())
            {
                var err = BuildErrorToken(skipped, index, asm);
                LinkChild(ref firstChild, ref prevChild, ref childCount, err, asm);
            }
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            int childIndex;
            if (child.IsToken)
            {
                var token = child.AsToken();
                if (token.IsKind(SyntaxKind.EndOfFileToken) && token.Span.Length == 0)
                {
                    continue;
                }

                childIndex = BuildToken(token, index, asm, wrapModifier: true);
            }
            else
            {
                childIndex = BuildSyntaxNode(child.AsNode()!, index, asm);
            }

            LinkChild(ref firstChild, ref prevChild, ref childCount, childIndex, asm);
        }

        var span = node.Span;
        asm.Commit(
            index,
            new ArenaNode
            {
                Index = index,
                Kind = SyntaxNodeKind.Rule,
                TypeId = (int)kind,
                TypeName = typeName,
                Start = span.Start,
                End = span.End,
                Parent = parent,
                FirstChild = firstChild,
                NextSibling = -1,
                ChildCount = childCount,
                IsNamed = true,
                ChainHead = index,
            },
            aliases
        );
        return index;
    }

    private static int BuildToken(SyntaxToken token, int parent, SyntaxTreeAssembler asm, bool wrapModifier)
    {
        if (wrapModifier && RoslynKindNames.IsModifierKeyword(token.Kind()) && IsModifierContext(token))
        {
            return BuildModifierWrapper(token, parent, asm);
        }

        return BuildRawToken(token, parent, asm);
    }

    private static bool IsModifierContext(SyntaxToken token)
    {
        // Only wrap when the token is a direct modifier of a declaration/parameter, not e.g. `new Foo()`.
        var parent = token.Parent;
        if (parent is null)
        {
            return false;
        }

        return parent
            is BaseTypeDeclarationSyntax
                or BaseMethodDeclarationSyntax
                or BaseFieldDeclarationSyntax
                or BasePropertyDeclarationSyntax
                or LocalDeclarationStatementSyntax
                or ParameterSyntax
                or DelegateDeclarationSyntax
                or EnumMemberDeclarationSyntax
                or IncompleteMemberSyntax
                or AccessorDeclarationSyntax
                or EventFieldDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                or OperatorDeclarationSyntax
                or IndexerDeclarationSyntax
                or PropertyDeclarationSyntax
                or EventDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DestructorDeclarationSyntax
                or MethodDeclarationSyntax
                or FieldDeclarationSyntax
                or ClassDeclarationSyntax
                or StructDeclarationSyntax
                or InterfaceDeclarationSyntax
                or RecordDeclarationSyntax
                or EnumDeclarationSyntax;
    }

    private static int BuildModifierWrapper(SyntaxToken token, int parent, SyntaxTreeAssembler asm)
    {
        var index = asm.Begin();
        var tokenIndex = BuildRawToken(token, index, asm);
        var span = token.Span;
        asm.Commit(
            index,
            new ArenaNode
            {
                Index = index,
                Kind = SyntaxNodeKind.Rule,
                TypeId = (int)token.Kind(),
                TypeName = "modifier",
                Start = span.Start,
                End = span.End,
                Parent = parent,
                FirstChild = tokenIndex,
                NextSibling = -1,
                ChildCount = 1,
                IsNamed = true,
                ChainHead = index,
            }
        );
        return index;
    }

    private static int BuildRawToken(SyntaxToken token, int parent, SyntaxTreeAssembler asm)
    {
        var index = asm.Begin();
        var kind = token.Kind();
        var typeName = RoslynKindNames.ToSnakeCase(kind);
        var span = token.Span;
        SyntaxNodeKind nodeKind;
        if (token.IsMissing)
        {
            nodeKind = SyntaxNodeKind.Missing;
            typeName = "MISSING";
        }
        else
        {
            nodeKind = SyntaxNodeKind.Token;
        }

        asm.Commit(
            index,
            new ArenaNode
            {
                Index = index,
                Kind = nodeKind,
                TypeId = (int)kind,
                TypeName = typeName,
                Start = span.Start,
                End = span.End,
                Parent = parent,
                FirstChild = -1,
                NextSibling = -1,
                ChildCount = 0,
                IsNamed = nodeKind == SyntaxNodeKind.Missing || RoslynKindNames.IsNamedToken(kind),
                ChainHead = index,
            }
        );
        return index;
    }

    private static int BuildErrorToken(SyntaxToken token, int parent, SyntaxTreeAssembler asm)
    {
        var index = asm.Begin();
        var span = token.Span;
        asm.Commit(
            index,
            new ArenaNode
            {
                Index = index,
                Kind = SyntaxNodeKind.Error,
                TypeId = (int)token.Kind(),
                TypeName = "ERROR",
                Start = span.Start,
                End = span.End,
                Parent = parent,
                FirstChild = -1,
                NextSibling = -1,
                ChildCount = 0,
                IsNamed = true,
                ChainHead = index,
            }
        );
        return index;
    }

    private static void LinkChild(
        ref int firstChild,
        ref int prevChild,
        ref int childCount,
        int childIndex,
        SyntaxTreeAssembler asm
    )
    {
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
}
