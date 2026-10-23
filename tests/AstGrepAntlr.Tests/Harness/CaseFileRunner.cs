using YamlDotNet.RepresentationModel;

namespace AstGrepAntlr.Tests.Harness;

public sealed record MatchExpectation(string? Text, Dictionary<string, string>? Meta);

public sealed record CaseFileEntry(
    string Id,
    string FilePath,
    string Language,
    string Pattern,
    string Source,
    string? Strictness,
    string? Entry,
    IReadOnlyList<MatchExpectation>? Expect,
    IReadOnlyList<string>? RejectTexts
);

/// <summary>Loads pattern match cases from tests/fixtures/cases/&lt;lang&gt;/*.yml.</summary>
public static class CaseFileRunner
{
    public static IEnumerable<object[]> Discover(string lang)
    {
        var dir = Fixtures.CasesDir(lang);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.yml").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var entry in LoadFile(file, lang))
            {
                yield return [entry.Id, entry];
            }
        }
    }

    public static IEnumerable<CaseFileEntry> LoadFile(string path, string? defaultLang = null)
    {
        var yaml = File.ReadAllText(path);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            yield break;
        }

        if (!TryGet(root, "cases", out var casesNode) || casesNode is not YamlSequenceNode seq)
        {
            yield break;
        }

        var lang = GetString(root, "language") ?? defaultLang ?? "java";
        var fileId = System.IO.Path.GetFileNameWithoutExtension(path);
        var index = 0;
        foreach (var item in seq)
        {
            if (item is not YamlMappingNode map)
            {
                continue;
            }

            var id = GetString(map, "id") ?? $"{fileId}/{index}";
            index++;
            var pattern = GetString(map, "pattern") ?? throw new InvalidOperationException($"{path}: pattern required");
            var source = GetString(map, "source") ?? throw new InvalidOperationException($"{path}: source required");
            List<MatchExpectation>? expect = null;
            if (TryGet(map, "expect", out var expectNode) && expectNode is YamlSequenceNode expectSeq)
            {
                expect = [];
                foreach (var e in expectSeq.OfType<YamlMappingNode>())
                {
                    Dictionary<string, string>? meta = null;
                    if (TryGet(e, "meta", out var metaNode) && metaNode is YamlMappingNode metaMap)
                    {
                        meta = metaMap.Children.ToDictionary(
                            kv => ((YamlScalarNode)kv.Key).Value!,
                            kv => ((YamlScalarNode)kv.Value).Value!,
                            StringComparer.Ordinal
                        );
                    }

                    expect.Add(new MatchExpectation(GetString(e, "text"), meta));
                }
            }

            List<string>? reject = null;
            if (TryGet(map, "reject", out var rejectNode) && rejectNode is YamlSequenceNode rejectSeq)
            {
                reject = rejectSeq.OfType<YamlScalarNode>().Select(s => s.Value!).ToList();
            }

            yield return new CaseFileEntry(
                id,
                path,
                GetString(map, "language") ?? lang,
                pattern,
                source,
                GetString(map, "strictness"),
                GetString(map, "entry"),
                expect,
                reject
            );
        }
    }

    private static bool TryGet(YamlMappingNode map, string key, out YamlNode node)
    {
        foreach (var (k, v) in map.Children)
        {
            if (k is YamlScalarNode s && s.Value == key)
            {
                node = v;
                return true;
            }
        }

        node = null!;
        return false;
    }

    private static string? GetString(YamlMappingNode map, string key) =>
        TryGet(map, key, out var n) && n is YamlScalarNode s ? s.Value : null;
}
