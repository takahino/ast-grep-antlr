using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AstGrepAntlr.DiffTest;

internal static class YamlConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static CorpusConfig LoadCorpus(string path) =>
        Deserializer.Deserialize<CorpusConfig>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Failed to parse corpus config: {path}");

    public static PatternFile LoadPatterns(string path) =>
        Deserializer.Deserialize<PatternFile>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Failed to parse pattern file: {path}");
}
