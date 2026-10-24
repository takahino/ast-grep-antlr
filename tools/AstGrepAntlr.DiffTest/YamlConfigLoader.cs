using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AstGrepAntlr.DiffTest;

internal static class YamlConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static CorpusConfig LoadCorpus(string path) =>
        Deserializer.Deserialize<CorpusConfig>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Failed to parse corpus config: {path}");

    public static PatternFile LoadPatterns(string path)
    {
        var file =
            Deserializer.Deserialize<PatternFile>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Failed to parse pattern file: {path}");
        foreach (var rule in file.Rules)
        {
            rule.Rule = NormalizeMap(rule.Rule);
        }

        return file;
    }

    public static KnownDifferencesFile LoadKnownDifferences(string path)
    {
        if (!File.Exists(path))
        {
            return new KnownDifferencesFile();
        }

        return Deserializer.Deserialize<KnownDifferencesFile>(File.ReadAllText(path)) ?? new KnownDifferencesFile();
    }

    public static string WriteRuleDocument(string id, string language, PatternRule rule)
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["language"] = language,
            ["rule"] = NormalizeMap(rule.Rule),
        };
        var fix = rule.EffectiveRewrite;
        if (!string.IsNullOrWhiteSpace(fix))
        {
            doc["fix"] = fix;
        }

        return Serializer.Serialize(doc);
    }

    public static Dictionary<string, object?> NormalizeMap(IDictionary<string, object?>? map)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (map is null)
        {
            return result;
        }

        foreach (var (key, value) in map)
        {
            result[key] = NormalizeValue(value);
        }

        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string or bool or int or long or double or float or decimal)
        {
            return value;
        }

        if (value is IDictionary<string, object?> sd)
        {
            return NormalizeMap(sd);
        }

        if (value is IDictionary<string, object> sd2)
        {
            return NormalizeMap(sd2.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal));
        }

        if (value is IDictionary<object, object?> od)
        {
            return NormalizeObjectMap(od);
        }

        if (value is IDictionary<object, object> od2)
        {
            return NormalizeObjectMap(od2.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>().Select(NormalizeValue).ToList();
        }

        return value.ToString();
    }

    private static Dictionary<string, object?> NormalizeObjectMap(IDictionary<object, object?> map)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            result[key?.ToString() ?? ""] = NormalizeValue(value);
        }

        return result;
    }
}
