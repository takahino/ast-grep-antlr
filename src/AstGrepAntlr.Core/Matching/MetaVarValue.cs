namespace AstGrepAntlr.Matching;

/// <summary>A captured metavariable with UTF-16 source range.</summary>
public sealed record MetaVarValue(string Text, int Start, int End, bool IsMulti);
