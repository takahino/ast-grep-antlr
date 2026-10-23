namespace AstGrepAntlr.DiffTest;

internal sealed record NormalizedMatch(
    string RelativePath,
    int ByteStart,
    int ByteEnd,
    string Text,
    IReadOnlyDictionary<string, string> MetaVariables
)
{
    public string RangeKey => $"{RelativePath}\0{ByteStart}\0{ByteEnd}";

    public string TextKey => $"{RelativePath}\0{Text}";
}

internal enum MatchBucket
{
    Same,
    OnlyAstGrep,
    OnlyAg4,
    RangeDiff,
    MetaVarDiff,
}

internal sealed record ClassifiedMatch(MatchBucket Bucket, NormalizedMatch? AstGrep, NormalizedMatch? Ag4);
