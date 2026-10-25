using AstGrepAntlr.Matching;

namespace AstGrepAntlr.Gui.Models;

/// <summary>One hit shown in the GUI, regardless of search mode. Lines and columns are 1-based.</summary>
public sealed class SearchHit
{
    public required string File { get; init; }
    public required string DisplayPath { get; init; }
    public required string Language { get; init; }
    public required int StartLine { get; init; }
    public required int StartColumn { get; init; }
    public required int EndLine { get; init; }
    public required int EndColumn { get; init; }

    /// <summary>UTF-16 offsets into the decoded file text.</summary>
    public required int Start { get; init; }
    public required int End { get; init; }
    public required string Text { get; init; }

    /// <summary>First line number of <see cref="ContextText"/>.</summary>
    public required int ContextStartLine { get; init; }
    public required string ContextText { get; init; }
    public IReadOnlyDictionary<string, string> MetaVariables { get; init; } = new Dictionary<string, string>();
    public string? RuleId { get; init; }
    public string? Message { get; init; }
    public string? Severity { get; init; }
    public string? Encoding { get; init; }

    /// <summary>Core match (AST / rule modes only); needed for rewrite.</summary>
    public MatchResult? Match { get; init; }

    /// <summary>Rule <c>fix</c> template, when the hit came from a rule that has one.</summary>
    public string? Fix { get; init; }

    public string MetaSummary => string.Join(", ", MetaVariables.Select(kv => $"${kv.Key}={OneLine(kv.Value)}"));

    public string TextOneLine => OneLine(Text);

    public string Location => $"{StartLine}:{StartColumn}";

    private static string OneLine(string text)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length > 200 ? flat[..200] + "…" : flat;
    }
}

public sealed record SearchOutcome(
    IReadOnlyList<SearchHit> Hits,
    IReadOnlyList<string> Warnings,
    TimeSpan Elapsed,
    bool Truncated
);
