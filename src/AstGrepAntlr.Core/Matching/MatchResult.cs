namespace AstGrepAntlr.Matching;

public sealed class MatchResult
{
    public required string File { get; init; }
    public required string Language { get; init; }

    /// <summary>UTF-16 start offset in decoded source (inclusive).</summary>
    public required int Start { get; init; }

    /// <summary>UTF-16 end offset in decoded source (exclusive).</summary>
    public required int End { get; init; }

    /// <summary>On-disk byte offset including BOM (inclusive).</summary>
    public int ByteStart { get; init; }

    /// <summary>On-disk byte offset including BOM (exclusive).</summary>
    public int ByteEnd { get; init; }

    /// <summary>1-based line for text output.</summary>
    public required int StartLine { get; init; }

    /// <summary>1-based column for text output.</summary>
    public required int StartColumn { get; init; }

    public required int EndLine { get; init; }
    public required int EndColumn { get; init; }

    /// <summary>0-based line for JSON (ast-grep compatible).</summary>
    public int StartLine0 => Math.Max(0, StartLine - 1);

    public int StartColumn0 => Math.Max(0, StartColumn - 1);
    public int EndLine0 => Math.Max(0, EndLine - 1);
    public int EndColumn0 => Math.Max(0, EndColumn - 1);

    public required string Text { get; init; }
    public required IReadOnlyDictionary<string, MetaVarValue> MetaVariables { get; init; }
    public string? RuleId { get; init; }
    public string? Message { get; init; }
    public string? Severity { get; init; }
    public string? Encoding { get; init; }
    public string? FileHash { get; init; }

    /// <summary>Surrounding source lines covering the match (ast-grep <c>lines</c>).</summary>
    public string? Lines { get; init; }

    public IReadOnlyDictionary<string, string> MetaTexts =>
        MetaVariables.ToDictionary(kv => kv.Key, kv => kv.Value.Text, StringComparer.Ordinal);
}
