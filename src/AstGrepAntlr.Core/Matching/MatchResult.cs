namespace AstGrepAntlr.Matching;

public sealed class MatchResult
{
    public required string File { get; init; }
    public required string Language { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
    public required int StartLine { get; init; }
    public required int StartColumn { get; init; }
    public required int EndLine { get; init; }
    public required int EndColumn { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyDictionary<string, string> MetaVariables { get; init; }
    public string? RuleId { get; init; }
    public string? Message { get; init; }
    public string? Severity { get; init; }
}
