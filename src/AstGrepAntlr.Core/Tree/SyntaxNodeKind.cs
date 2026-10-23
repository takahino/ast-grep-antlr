namespace AstGrepAntlr.Tree;

/// <summary>Classification of a syntax node in the arena tree.</summary>
public enum SyntaxNodeKind : byte
{
    Rule = 0,
    Token = 1,
    Error = 2,
    Missing = 3,
}
