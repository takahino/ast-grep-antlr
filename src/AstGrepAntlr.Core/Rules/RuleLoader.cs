using YamlDotNet.RepresentationModel;

namespace AstGrepAntlr.Rules;

public static class RuleLoader
{
    public static SgRuleDocument LoadRule(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Rule YAML must be a mapping.");
        }

        var doc = new SgRuleDocument
        {
            Id = GetString(root, "id"),
            Language = GetString(root, "language"),
            Severity = GetString(root, "severity"),
            Message = GetString(root, "message"),
            Note = GetString(root, "note"),
            Fix = GetString(root, "fix"),
            Files = GetStringList(root, "files"),
            Ignores = GetStringList(root, "ignores"),
        };

        if (TryGet(root, "rule", out var ruleNode))
        {
            doc.Rule = ParseRule(ruleNode);
        }

        if (TryGet(root, "constraints", out var constraints) && constraints is YamlMappingNode cmap)
        {
            doc.Constraints = cmap.Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv => ParseRule(kv.Value)!,
                StringComparer.Ordinal
            );
        }

        if (TryGet(root, "utils", out var utils) && utils is YamlMappingNode umap)
        {
            doc.Utils = umap.Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv => ParseRule(kv.Value)!,
                StringComparer.Ordinal
            );
        }

        return doc;
    }

    public static SgConfig LoadConfig(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new SgConfig();
        }

        var config = new SgConfig
        {
            RuleDirs = GetStringList(root, "ruleDirs"),
            UtilDirs = GetStringList(root, "utilDirs"),
        };

        if (TryGet(root, "customLanguages", out var customs) && customs is YamlMappingNode cmap)
        {
            config.CustomLanguages = new Dictionary<string, CustomLanguageConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in cmap.Children)
            {
                if (value is not YamlMappingNode m)
                {
                    continue;
                }

                config.CustomLanguages[((YamlScalarNode)key).Value!] = new CustomLanguageConfig
                {
                    Grammar = GetStringList(m, "grammar"),
                    Extensions = GetStringList(m, "extensions"),
                    StartRule = GetString(m, "startRule"),
                    PatternRules = GetStringList(m, "patternRules"),
                    MetaVarPlaceholder = GetString(m, "metaVarPlaceholder"),
                    IgnoreCase = string.Equals(GetString(m, "ignoreCase"), "true", StringComparison.OrdinalIgnoreCase),
                };
            }
        }

        return config;
    }

    public static IEnumerable<SgRuleDocument> LoadRuleDir(string dir)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (
            var file in Directory
                .EnumerateFiles(dir, "*.yml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories))
        )
        {
            yield return LoadRule(File.ReadAllText(file));
        }
    }

    private static RuleNode? ParseRule(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            return new RuleNode { Pattern = scalar.Value };
        }

        if (node is not YamlMappingNode map)
        {
            return null;
        }

        var rule = new RuleNode
        {
            Kind = GetString(map, "kind"),
            Regex = GetString(map, "regex"),
            Matches = GetString(map, "matches"),
            StopBy = GetString(map, "stopBy"),
        };

        if (TryGet(map, "pattern", out var patternNode))
        {
            if (patternNode is YamlScalarNode ps)
            {
                rule.Pattern = ps.Value;
            }
            else if (patternNode is YamlMappingNode pm)
            {
                rule.PatternObject = new PatternObject
                {
                    Context = GetString(pm, "context"),
                    Selector = GetString(pm, "selector"),
                    Strictness = GetString(pm, "strictness"),
                    Text = GetString(pm, "text"),
                    Entry = GetString(pm, "entry"),
                };
            }
        }

        if (TryGet(map, "inside", out var inside))
        {
            rule.Inside = ParseRule(inside);
        }

        if (TryGet(map, "has", out var has))
        {
            rule.Has = ParseRule(has);
        }

        if (TryGet(map, "precedes", out var precedes))
        {
            rule.Precedes = ParseRule(precedes);
        }

        if (TryGet(map, "follows", out var follows))
        {
            rule.Follows = ParseRule(follows);
        }

        if (TryGet(map, "not", out var not))
        {
            rule.Not = ParseRule(not);
        }

        if (TryGet(map, "all", out var all) && all is YamlSequenceNode allSeq)
        {
            rule.All = allSeq.Children.Select(ParseRule).Where(r => r is not null).Cast<RuleNode>().ToList();
        }

        if (TryGet(map, "any", out var any) && any is YamlSequenceNode anySeq)
        {
            rule.Any = anySeq.Children.Select(ParseRule).Where(r => r is not null).Cast<RuleNode>().ToList();
        }

        return rule;
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

    private static List<string>? GetStringList(YamlMappingNode map, string key)
    {
        if (!TryGet(map, key, out var n))
        {
            return null;
        }

        if (n is YamlSequenceNode seq)
        {
            return seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).Where(v => v is not null).ToList()!;
        }

        if (n is YamlScalarNode scalar && scalar.Value is not null)
        {
            return [scalar.Value];
        }

        return null;
    }
}
