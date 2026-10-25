using System.Text.Json.Serialization;

namespace AstGrepAntlr.Native;

internal sealed class SearchRequest
{
    public string? Pattern { get; set; }
    public string? Language { get; set; }
    public string[]? Paths { get; set; }

    /// <summary>In-memory sources from a frontend; when non-empty, <see cref="Paths"/> is ignored.</summary>
    public SourceDto[]? Sources { get; set; }
    public string? Strictness { get; set; }
    public int? MaxResults { get; set; }

    /// <summary>Optional parser backend: tree-sitter, antlr, or roslyn (switchable languages only).</summary>
    public string? Backend { get; set; }
}

internal sealed class SourceDto
{
    /// <summary>Display path / label in match results (not read from disk).</summary>
    public string? Path { get; set; }
    public string? Content { get; set; }
}

internal sealed class ErrorResponse
{
    public required string Error { get; set; }
}

internal sealed class MatchDto
{
    public required string Text { get; set; }
    public required string File { get; set; }
    public required string Language { get; set; }
    public required RangeDto Range { get; set; }
    public MetaVariablesDto? MetaVariables { get; set; }
    public string? RuleId { get; set; }
    public string? Message { get; set; }
    public string? Severity { get; set; }
    public string? Lines { get; set; }
    public string? Encoding { get; set; }
}

internal sealed class RangeDto
{
    public required OffsetPairDto ByteOffset { get; set; }
    public required OffsetPairDto CharOffset { get; set; }
    public required LineColDto Start { get; set; }
    public required LineColDto End { get; set; }
}

internal sealed class OffsetPairDto
{
    public required int Start { get; set; }
    public required int End { get; set; }
}

internal sealed class LineColDto
{
    public required int Line { get; set; }
    public required int Column { get; set; }
}

internal sealed class MetaVariablesDto
{
    public Dictionary<string, MetaEntryDto>? Single { get; set; }
    public Dictionary<string, MetaEntryDto[]>? Multi { get; set; }
}

internal sealed class MetaEntryDto
{
    public required string Text { get; set; }
    public required RangeDto Range { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SourceDto))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(MatchDto[]))]
[JsonSerializable(typeof(MatchDto))]
[JsonSerializable(typeof(Dictionary<string, MetaEntryDto>))]
[JsonSerializable(typeof(Dictionary<string, MetaEntryDto[]>))]
internal partial class NativeJsonContext : JsonSerializerContext;
