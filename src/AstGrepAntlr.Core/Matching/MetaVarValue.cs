namespace AstGrepAntlr.Matching;

/// <summary>One captured node (or token) within a metavariable.</summary>
public sealed record MetaVarSpan(
    string Text,
    int Start,
    int End,
    int ByteStart = 0,
    int ByteEnd = 0,
    int StartLine0 = 0,
    int StartColumn0 = 0,
    int EndLine0 = 0,
    int EndColumn0 = 0
);

/// <summary>A captured metavariable with source ranges.</summary>
/// <param name="Text">Contiguous source slice (for rewrite / display).</param>
/// <param name="Start">UTF-16 start of the contiguous slice.</param>
/// <param name="End">UTF-16 end of the contiguous slice.</param>
/// <param name="IsMulti">True for <c>$$$</c> captures.</param>
/// <param name="Parts">
/// Per-node pieces for multi metavars (ast-grep JSON <c>multi</c> array shape).
/// Null for single metavars or when parts were not recorded.
/// </param>
/// <param name="ByteStart">On-disk byte offset of the contiguous slice (inclusive).</param>
/// <param name="ByteEnd">On-disk byte offset of the contiguous slice (exclusive).</param>
/// <param name="StartLine0">0-based start line of the contiguous slice.</param>
/// <param name="StartColumn0">0-based start column of the contiguous slice.</param>
/// <param name="EndLine0">0-based end line of the contiguous slice.</param>
/// <param name="EndColumn0">0-based end column of the contiguous slice.</param>
public sealed record MetaVarValue(
    string Text,
    int Start,
    int End,
    bool IsMulti,
    IReadOnlyList<MetaVarSpan>? Parts = null,
    int ByteStart = 0,
    int ByteEnd = 0,
    int StartLine0 = 0,
    int StartColumn0 = 0,
    int EndLine0 = 0,
    int EndColumn0 = 0
);
