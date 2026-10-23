namespace AstGrepAntlr.Tree;

/// <summary>Compact node stored in a <see cref="SyntaxTree"/> arena.</summary>
public readonly struct SyntaxNode
{
    public required int Index { get; init; }
    public required SyntaxNodeKind Kind { get; init; }
    public required int TypeId { get; init; }
    public required string TypeName { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
    public required int Parent { get; init; }
    public required int FirstChild { get; init; }
    public required int NextSibling { get; init; }
    public required int ChildCount { get; init; }
    public required bool IsNamed { get; init; }
    public required int ChainHead { get; init; }
    public string? MetaVarName { get; init; }
    public bool IsMultiMetaVar { get; init; }

    public bool IsTerminal => Kind is SyntaxNodeKind.Token or SyntaxNodeKind.Error or SyntaxNodeKind.Missing;
    public bool IsError => Kind is SyntaxNodeKind.Error or SyntaxNodeKind.Missing;
}
