using System.ComponentModel;
using System.Text.Json;
using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Output;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Search;
using AstGrepAntlr.Tree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace AstGrepAntlr.Mcp;

public static class McpHost
{
    public static async Task RunAsync(
        LanguageRegistry registry,
        IReadOnlyList<string> roots,
        bool allowWrite,
        CancellationToken ct = default
    )
    {
        McpContext.Registry = registry;
        McpContext.Roots = roots.Select(Path.GetFullPath).ToList();
        McpContext.AllowWrite = allowWrite;

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        var mcp = builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<Ag4Tools>();
        if (allowWrite)
        {
            mcp.WithTools<Ag4WriteTools>();
        }

        var host = builder.Build();
        await host.RunAsync(ct);
    }
}

internal static class McpContext
{
    public static LanguageRegistry Registry { get; set; } = new();
    public static List<string> Roots { get; set; } = [];
    public static bool AllowWrite { get; set; }
    public const int DefaultMaxResults = 50;
    public const int MaxBytes = 512_000;

    /// <summary>Defaults to the allowed roots when no paths are given, and validates every one.</summary>
    public static string[] ResolvePaths(string[]? paths)
    {
        paths ??= Roots.ToArray();
        foreach (var p in paths)
        {
            EnsureUnderRoot(p);
        }

        return paths;
    }

    public static void EnsureUnderRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (
            !Roots.Any(r =>
                full.StartsWith(
                    r.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                ) || string.Equals(full, r, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException($"Path '{path}' is outside allowed --root directories.");
        }
    }
}

[McpServerToolType]
public sealed class Ag4Tools
{
    [McpServerTool(Name = "list_languages", ReadOnly = true), Description("List built-in and custom languages.")]
    public static string ListLanguages()
    {
        var list = McpContext.Registry.All.Select(l => new
        {
            id = l.Id,
            extensions = l.Extensions,
            startRule = l.StartRule,
            patternRules = l.PatternRules,
            ignoreCase = l.IgnoreCase,
        });
        return JsonSerializer.Serialize(list);
    }

    [
        McpServerTool(Name = "describe_language", ReadOnly = true),
        Description("Describe parser rule names, tokens, and pattern configuration.")
    ]
    public static string DescribeLanguage([Description("Language id")] string language)
    {
        var lang = McpContext.Registry.Get(language);
        var tokens = new List<object>();
        for (var i = 1; i <= lang.MaxTokenType; i++)
        {
            var sym = lang.Vocabulary.GetSymbolicName(i);
            var lit = lang.Vocabulary.GetLiteralName(i);
            if (sym is null && lit is null)
            {
                continue;
            }

            tokens.Add(
                new
                {
                    type = i,
                    symbolic = sym,
                    literal = lit,
                }
            );
        }

        return JsonSerializer.Serialize(
            new
            {
                id = lang.Id,
                extensions = lang.Extensions,
                startRule = lang.StartRule,
                patternRules = lang.PatternRules,
                metaVarPlaceholder = lang.MetaVarPlaceholderTemplate,
                ignoreCase = lang.IgnoreCase,
                ruleNames = lang.RuleNames,
                tokens,
            }
        );
    }

    [
        McpServerTool(Name = "dump_syntax_tree", ReadOnly = true),
        Description("Dump a syntax tree for code or a pattern.")
    ]
    public static string DumpSyntaxTree(
        [Description("Source code or pattern text")] string code,
        [Description("Language id")] string language,
        [Description("Mode: cst, pattern")] string mode = "cst",
        [Description("Optional entry rule")] string? entry = null
    )
    {
        var lang = McpContext.Registry.Get(language);
        SyntaxTree tree;
        int root;
        if (string.Equals(mode, "pattern", StringComparison.OrdinalIgnoreCase))
        {
            var compiled = new PatternCompiler().Compile(lang, new PatternSpec(code, entry));
            tree = compiled.Tree;
            root = compiled.Root;
        }
        else
        {
            var parsed = new TwoStageParser().Parse(lang, code, new LanguageParseOptions { EntryRule = entry });
            tree = parsed.Tree;
            root = parsed.Tree.Root;
        }

        var text = TreeDumper.ToText(tree, root);
        return text.Length > McpContext.MaxBytes ? text[..McpContext.MaxBytes] + "\n...[truncated]" : text;
    }

    [McpServerTool(Name = "find_code", ReadOnly = true), Description("Search files with an AST pattern.")]
    public static string FindCode(
        [Description("AST pattern")] string pattern,
        [Description("Language id")] string language,
        [Description("Paths under --root")] string[]? paths = null,
        [Description("Strictness")] string strictness = "smart",
        [Description("Max results")] int max_results = 50
    )
    {
        paths = McpContext.ResolvePaths(paths);

        var s = Enum.TryParse<Strictness>(strictness, true, out var st) ? st : Strictness.Smart;
        var searcher = new Searcher();
        var matches = searcher
            .RunPattern(
                pattern,
                new SearchOptions
                {
                    Registry = McpContext.Registry,
                    LanguageId = language,
                    Paths = paths,
                    MaxResults = Math.Min(max_results, McpContext.DefaultMaxResults * 2),
                },
                s
            )
            .ToList();

        return SerializeMatches(matches, max_results);
    }

    [McpServerTool(Name = "find_code_by_rule", ReadOnly = true), Description("Search with a YAML rule document.")]
    public static string FindCodeByRule(
        [Description("Rule YAML")] string rule_yaml,
        string[]? paths = null,
        int max_results = 50
    )
    {
        paths = McpContext.ResolvePaths(paths);

        var doc = RuleLoader.LoadRule(rule_yaml);
        var matches = new Searcher()
            .RunRule(
                doc,
                new SearchOptions
                {
                    Registry = McpContext.Registry,
                    Paths = paths,
                    MaxResults = max_results,
                }
            )
            .ToList();
        return SerializeMatches(matches, max_results);
    }

    [
        McpServerTool(Name = "test_match_code_rule", ReadOnly = true),
        Description("Test a rule against an in-memory code snippet.")
    ]
    public static string TestMatchCodeRule(
        [Description("Code snippet")] string code,
        [Description("Rule YAML")] string rule_yaml
    )
    {
        var doc = RuleLoader.LoadRule(rule_yaml);
        if (doc.Language is null || doc.Rule is null)
        {
            return "[]";
        }

        var lang = McpContext.Registry.Get(doc.Language);
        var tree = new TwoStageParser().Parse(lang, code).Tree;
        var hits = new RuleEngine().Match(lang, tree, doc).ToList();
        var results = hits.Select(h => new
        {
            text = tree.Text(h.NodeIndex),
            meta = h.Meta,
            start = tree[h.NodeIndex].Start,
            end = tree[h.NodeIndex].End,
        });
        return JsonSerializer.Serialize(results);
    }

    [McpServerTool(Name = "scan", ReadOnly = true), Description("Scan paths with sgconfig or rule dirs.")]
    public static string Scan(string? config = null, string[]? paths = null, int max_results = 50)
    {
        paths = McpContext.ResolvePaths(paths);

        var docs = new List<SgRuleDocument>();
        if (!string.IsNullOrEmpty(config))
        {
            McpContext.EnsureUnderRoot(config);
            var cfg = RuleLoader.LoadConfig(File.ReadAllText(config));
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(config))!;
            foreach (var dir in cfg.RuleDirs ?? [])
            {
                docs.AddRange(RuleLoader.LoadRuleDir(Path.Combine(baseDir, dir)));
            }
        }

        var searcher = new Searcher();
        var all = new List<MatchResult>();
        foreach (var doc in docs)
        {
            all.AddRange(
                searcher.RunRule(
                    doc,
                    new SearchOptions
                    {
                        Registry = McpContext.Registry,
                        Paths = paths,
                        MaxResults = max_results,
                    }
                )
            );
            if (all.Count >= max_results)
            {
                break;
            }
        }

        return SerializeMatches(all, max_results);
    }

    [
        McpServerTool(Name = "rewrite_preview", ReadOnly = true),
        Description("Preview rewrites as unified diffs without writing files.")
    ]
    public static string RewritePreview(
        string? pattern = null,
        string? rule_yaml = null,
        string? rewrite = null,
        string? language = null,
        string[]? paths = null
    )
    {
        paths = McpContext.ResolvePaths(paths);

        List<MatchResult> matches;
        string? fix = rewrite;
        if (!string.IsNullOrEmpty(rule_yaml))
        {
            var doc = RuleLoader.LoadRule(rule_yaml);
            fix ??= doc.Fix;
            matches = new Searcher()
                .RunRule(doc, new SearchOptions { Registry = McpContext.Registry, Paths = paths })
                .ToList();
        }
        else if (!string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(language))
        {
            matches = new Searcher()
                .RunPattern(
                    pattern!,
                    new SearchOptions
                    {
                        Registry = McpContext.Registry,
                        LanguageId = language,
                        Paths = paths,
                    }
                )
                .ToList();
        }
        else
        {
            throw new ArgumentException("Provide pattern+language or rule_yaml.");
        }

        if (string.IsNullOrEmpty(fix))
        {
            throw new ArgumentException("rewrite template is required (or rule fix).");
        }

        var changes = new List<object>();
        foreach (var group in matches.GroupBy(m => m.File))
        {
            var change = Rewriter.RewriteFile(
                group.Key,
                group,
                m => Rewriter.ApplyTemplate(fix!, m.MetaTexts, m.Text),
                apply: false
            );
            changes.Add(
                new
                {
                    file = change.File,
                    hash = change.OriginalHash,
                    encoding = change.Detected.Name,
                    diff = change.UnifiedDiff,
                }
            );
        }

        return JsonSerializer.Serialize(changes);
    }

    private static string SerializeMatches(List<MatchResult> matches, int max)
    {
        var truncated = matches.Count > max;
        var slice = matches.Take(max).ToList();
        using var sw = new StringWriter();
        JsonPrinter.Print(slice, sw);
        var json = sw.ToString();
        if (truncated)
        {
            json = $"{{\"truncated\":true,\"count\":{matches.Count},\"matches\":{json.Trim()}}}";
        }

        if (json.Length > McpContext.MaxBytes)
        {
            json = json[..McpContext.MaxBytes] + "...[truncated]";
        }

        return json;
    }
}

[McpServerToolType]
public sealed class Ag4WriteTools
{
    [
        McpServerTool(Name = "rewrite_apply", Destructive = true),
        Description("Apply rewrites. Requires --allow-write and preview hashes.")
    ]
    public static string RewriteApply(
        [Description("JSON array of {file, hash} from rewrite_preview")] string preview_json,
        string? pattern = null,
        string? rule_yaml = null,
        string? rewrite = null,
        string? language = null
    )
    {
        using var doc = JsonDocument.Parse(preview_json);
        var applied = new List<string>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var file = el.GetProperty("file").GetString()!;
            var hash = el.GetProperty("hash").GetString()!;
            McpContext.EnsureUnderRoot(file);

            List<MatchResult> matches;
            string? fix = rewrite;
            if (!string.IsNullOrEmpty(rule_yaml))
            {
                var rule = RuleLoader.LoadRule(rule_yaml);
                fix ??= rule.Fix;
                matches = new Searcher()
                    .RunRule(rule, new SearchOptions { Registry = McpContext.Registry, Paths = [file] })
                    .ToList();
            }
            else
            {
                matches = new Searcher()
                    .RunPattern(
                        pattern!,
                        new SearchOptions
                        {
                            Registry = McpContext.Registry,
                            LanguageId = language,
                            Paths = [file],
                        }
                    )
                    .ToList();
            }

            var change = Rewriter.RewriteFile(
                file,
                matches,
                m => Rewriter.ApplyTemplate(fix!, m.MetaTexts, m.Text),
                apply: false
            );
            if (!string.Equals(change.OriginalHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"File '{file}' changed since preview (hash mismatch). Aborting apply."
                );
            }

            Rewriter.ApplyAtomic(file, change.NewText, hash, change.Detected);
            applied.Add(file);
        }

        return JsonSerializer.Serialize(new { applied });
    }
}
