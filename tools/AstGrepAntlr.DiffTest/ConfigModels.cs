namespace AstGrepAntlr.DiffTest;

internal sealed class CorpusConfig
{
    public int Version { get; set; } = 1;

    public string Language { get; set; } = "java";

    public List<CorpusRepo> Repos { get; set; } = [];
}

internal sealed class CorpusRepo
{
    public required string Id { get; set; }

    public required string Remote { get; set; }

    public required string Ref { get; set; }

    public string? Sha { get; set; }
}

internal sealed class PatternFile
{
    public string Language { get; set; } = "java";

    public List<PatternRule> Rules { get; set; } = [];
}

internal sealed class PatternRule
{
    public required string Id { get; set; }

    public PatternRuleBody Rule { get; set; } = new();
}

internal sealed class PatternRuleBody
{
    public string? Pattern { get; set; }
}
