namespace AstGrepAntlr.DiffTest;

internal sealed class DiffTestReport
{
    public required string GeneratedAtUtc { get; set; }

    public required string Mode { get; set; }

    public required string AstGrepVersion { get; set; }

    public required CorpusConfig Corpus { get; set; }

    public required List<PreparedRepoReport> Repositories { get; set; }

    public required List<PatternReport> Patterns { get; set; }

    public required DiffSummary Summary { get; set; }
}

internal sealed class PreparedRepoReport
{
    public required string Id { get; set; }

    public required string Path { get; set; }

    public required string Ref { get; set; }

    public required string Sha { get; set; }
}

internal sealed class PatternReport
{
    public required string Id { get; set; }

    public required string Pattern { get; set; }

    public required string RepoId { get; set; }

    public string? AstGrepError { get; set; }

    public string? Ag4Error { get; set; }

    public required DiffSummary Counts { get; set; }

    public required List<ClassifiedMatchReport> Samples { get; set; }
}

internal sealed class ClassifiedMatchReport
{
    public required string Bucket { get; set; }

    public NormalizedMatchReport? AstGrep { get; set; }

    public NormalizedMatchReport? Ag4 { get; set; }
}

internal sealed class NormalizedMatchReport
{
    public required string RelativePath { get; set; }

    public required int ByteStart { get; set; }

    public required int ByteEnd { get; set; }

    public required string Text { get; set; }

    public required Dictionary<string, string> MetaVariables { get; set; }
}

internal sealed class DiffSummary
{
    public int Same { get; set; }

    public int OnlyAstGrep { get; set; }

    public int OnlyAg4 { get; set; }

    public int RangeDiff { get; set; }

    public int MetaVarDiff { get; set; }

    public void Add(ClassifiedMatch match)
    {
        switch (match.Bucket)
        {
            case MatchBucket.Same:
                Same++;
                break;
            case MatchBucket.OnlyAstGrep:
                OnlyAstGrep++;
                break;
            case MatchBucket.OnlyAg4:
                OnlyAg4++;
                break;
            case MatchBucket.RangeDiff:
                RangeDiff++;
                break;
            case MatchBucket.MetaVarDiff:
                MetaVarDiff++;
                break;
        }
    }

    public void Add(DiffSummary other)
    {
        Same += other.Same;
        OnlyAstGrep += other.OnlyAstGrep;
        OnlyAg4 += other.OnlyAg4;
        RangeDiff += other.RangeDiff;
        MetaVarDiff += other.MetaVarDiff;
    }
}
