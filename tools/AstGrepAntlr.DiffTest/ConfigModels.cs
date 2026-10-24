namespace AstGrepAntlr.DiffTest;

internal sealed class CorpusConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Default language when a repo omits <see cref="CorpusRepo.Language"/>.</summary>
    public string Language { get; set; } = "java";

    public List<CorpusRepo> Repos { get; set; } = [];
}

internal sealed class CorpusRepo
{
    public required string Id { get; set; }

    public required string Remote { get; set; }

    public required string Ref { get; set; }

    public string? Sha { get; set; }

    public string? Language { get; set; }

    /// <summary>Optional relative subpaths to search (defaults to repo root).</summary>
    public List<string> Paths { get; set; } = [];
}

internal sealed class PatternFile
{
    public string Language { get; set; } = "java";

    public List<PatternRule> Rules { get; set; } = [];
}

internal sealed class PatternRule
{
    public required string Id { get; set; }

    /// <summary>Raw rule mapping (pattern / all / any / inside / …).</summary>
    public Dictionary<string, object?> Rule { get; set; } = new(StringComparer.Ordinal);

    public string? Rewrite { get; set; }

    public string? Fix { get; set; }

    public string? SimplePattern =>
        IsSimplePatternRule() && Rule.TryGetValue("pattern", out var p) && p is string s ? s : null;

    public string EffectiveRewrite => Rewrite ?? Fix ?? "";

    public bool HasRewrite => !string.IsNullOrWhiteSpace(Rewrite) || !string.IsNullOrWhiteSpace(Fix);

    public bool IsSimplePatternRule() => Rule.Count == 1 && Rule.TryGetValue("pattern", out var p) && p is string;
}

internal sealed class KnownDifferencesFile
{
    public int Version { get; set; } = 1;

    public List<KnownDifference> Differences { get; set; } = [];
}

internal sealed class KnownDifference
{
    public required string Id { get; set; }

    public string? Language { get; set; }

    public string? PatternId { get; set; }

    public string? RepoId { get; set; }

    public string? Category { get; set; }

    public string? Reason { get; set; }

    /// <summary>Optional bucket filter: OnlyAstGrep, OnlyAg4, RangeDiff, MetaVarDiff.</summary>
    public string? Bucket { get; set; }
}
