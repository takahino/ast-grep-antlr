using System.Diagnostics;
using System.Text.RegularExpressions;
using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.Config;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Search;

namespace AstGrepAntlr.Gui.Services;

/// <summary>Search root resolved to an absolute path plus the directory globs/display paths are relative to.</summary>
public sealed record ResolvedRoot(string Path, string BaseDir);

/// <summary>Runs one search synchronously; callers put it on a worker thread.</summary>
public sealed class SearchService
{
    private static readonly char[] ListSeparators = [';', ',', '\n', '\r'];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private string? _registryKey;
    private LanguageRegistry? _registry;

    /// <summary>Canonical ids of every built-in language, for the language picker.</summary>
    public static IReadOnlyList<string> BuiltinLanguageIds() =>
        AllLanguages(BuiltinLanguages.CreateRegistry())
            .Select(l => l.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public SearchOutcome Run(SearchRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var sw = Stopwatch.StartNew();
        var root = ResolveRoot(req.Root);
        var warnings = new List<string>();
        var limit = req.MaxResults > 0 ? req.MaxResults : int.MaxValue;
        if (req.Mode != SearchMode.Rule || req.RuleSource == RuleSourceKind.Inline)
        {
            if (string.IsNullOrWhiteSpace(req.Pattern))
            {
                throw new ArgumentException("Pattern is empty.");
            }
        }

        var hits = req.Mode switch
        {
            SearchMode.Ast => RunAst(req, root, limit, warnings, ct),
            SearchMode.Rule => RunRules(req, root, limit, warnings, ct),
            _ => TextSearch.Run(req, root, limit, warnings, ct),
        };

        var truncated = hits.Count > limit;
        if (truncated)
        {
            hits = hits.Take(limit).ToList();
        }

        return new SearchOutcome(hits, warnings, sw.Elapsed, truncated);
    }

    public static ResolvedRoot ResolveRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Search root is empty. Choose a folder or file to search.");
        }

        var full = Path.GetFullPath(root.Trim());
        if (File.Exists(full))
        {
            return new ResolvedRoot(full, Path.GetDirectoryName(full)!);
        }

        if (Directory.Exists(full))
        {
            return new ResolvedRoot(full, full);
        }

        throw new DirectoryNotFoundException($"Search root not found: {full}");
    }

    /// <summary>
    /// The encoding name to force when reading files: the explicit choice, else sgconfig's <c>encoding</c>,
    /// else null (auto-detect). Rewrite must read files the same way the search did so offsets line up.
    /// </summary>
    public static string? EffectiveEncoding(SearchRequest req, string? configPath) =>
        req.Encoding != SearchRequest.AutoEncoding && !string.IsNullOrWhiteSpace(req.Encoding)
            ? req.Encoding
            : SgConfigSupport.LoadConfigEncoding(configPath);

    /// <summary>sgconfig.yml governing this search (custom languages, utils, encoding), if any.</summary>
    public static string? FindConfig(SearchRequest req, ResolvedRoot root)
    {
        if (req.Mode == SearchMode.Rule && req.RuleSource == RuleSourceKind.Config && !IsBlank(req.RulePath))
        {
            var explicitPath = Path.GetFullPath(req.RulePath.Trim());
            return File.Exists(explicitPath)
                ? explicitPath
                : throw new FileNotFoundException($"sgconfig not found: {explicitPath}");
        }

        return SgConfigLocator.FindUpwards(root.BaseDir);
    }

    public static IReadOnlyList<string> BuildGlobs(SearchRequest req)
    {
        var globs = SplitList(req.Globs).ToList();
        // PathGlob treats a leading '!' as an exclude; "**/name/**" skips the directory at any depth.
        globs.AddRange(SplitList(req.ExcludeDirs).Select(d => $"!**/{d.Trim('/', '\\')}/**"));
        return globs;
    }

    private List<SearchHit> RunAst(
        SearchRequest req,
        ResolvedRoot root,
        int limit,
        List<string> warnings,
        CancellationToken ct
    )
    {
        var config = FindConfig(req, root);
        var registry = GetRegistry(config, req.Backend, warnings);
        var encoding = EffectiveEncoding(req, config);
        if (!Enum.TryParse<Strictness>(req.Strictness, ignoreCase: true, out var strictness))
        {
            strictness = Strictness.Smart;
        }

        var searcher = new Searcher();
        if (!string.Equals(req.Language, SearchRequest.AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var options = Options(req, root, registry, req.Language, [root.Path], limit, encoding, null, ct);
            return Project(searcher.RunPattern(req.Pattern, options, strictness), root, req, null);
        }

        // Auto: assign each file extension present under the root to the first language whose
        // extensions include it AND that can compile the pattern, so a file is searched once.
        var files = new FileWalker()
            .Enumerate(
                [root.Path],
                new FileWalkOptions
                {
                    Globs = BuildGlobs(req),
                    NoIgnore = req.NoIgnore,
                    IncludeHidden = req.IncludeHidden,
                    BaseDirectory = root.BaseDir,
                }
            )
            .ToList();
        ct.ThrowIfCancellationRequested();
        var byExt = files
            .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.ToList());

        var compiler = new PatternCompiler();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<SearchHit>();
        foreach (var language in AllLanguages(registry))
        {
            ct.ThrowIfCancellationRequested();
            var exts = language
                .Extensions.Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
                .Where(e => byExt.ContainsKey(e) && !claimed.Contains(e))
                .Distinct()
                .ToList();
            if (exts.Count == 0)
            {
                continue;
            }

            try
            {
                compiler.Compile(language, new PatternSpec(req.Pattern, null, Strictness: strictness));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"{language.Id}: pattern skipped ({ex.Message})");
                continue;
            }

            claimed.UnionWith(exts);
            var paths = exts.SelectMany(e => byExt[e]).ToList();
            var options = Options(req, root, registry, language.Id, paths, limit - hits.Count, encoding, null, ct);
            hits.AddRange(Project(searcher.RunPattern(req.Pattern, options, strictness), root, req, null));
            if (hits.Count > limit)
            {
                break;
            }
        }

        return SortHits(hits);
    }

    private List<SearchHit> RunRules(
        SearchRequest req,
        ResolvedRoot root,
        int limit,
        List<string> warnings,
        CancellationToken ct
    )
    {
        var config = FindConfig(req, root);
        var registry = GetRegistry(config, req.Backend, warnings);
        var encoding = EffectiveEncoding(req, config);
        var warn = new StringWriter();
        Dictionary<string, RuleNode>? utils = null;
        string? configDir = null;
        if (config is not null)
        {
            var cfg = RuleLoader.LoadConfig(File.ReadAllText(config));
            configDir = Path.GetDirectoryName(config)!;
            utils = SgConfigSupport.LoadUtilDirs(cfg, configDir);
        }

        var docs = new List<SgRuleDocument>();
        switch (req.RuleSource)
        {
            case RuleSourceKind.Config:
                if (config is null)
                {
                    throw new FileNotFoundException(
                        "sgconfig.yml was not found above the search root. Specify it, or choose another rule source."
                    );
                }

                var ruleCfg = RuleLoader.LoadConfig(File.ReadAllText(config));
                foreach (var dir in ruleCfg.RuleDirs ?? [])
                {
                    foreach (var loaded in RuleLoader.LoadRuleDir(Path.Combine(configDir!, dir)))
                    {
                        docs.Add(SgConfigSupport.EnsureRuleId(loaded.Document, loaded.Path, warn));
                    }
                }

                break;
            case RuleSourceKind.File:
                if (IsBlank(req.RulePath))
                {
                    throw new ArgumentException("Rule file is empty. Choose a rule YAML file.");
                }

                var rulePath = Path.GetFullPath(req.RulePath.Trim());
                docs.Add(SgConfigSupport.EnsureRuleId(RuleLoader.LoadRule(File.ReadAllText(rulePath)), rulePath, warn));
                break;
            case RuleSourceKind.Inline:
                var chunks = Regex.Split(req.Pattern, @"^---[ \t]*\r?$", RegexOptions.Multiline, RegexTimeout);
                var index = 0;
                foreach (var chunk in chunks.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    index++;
                    docs.Add(SgConfigSupport.EnsureRuleId(RuleLoader.LoadRule(chunk), $"inline-{index}", null));
                }

                break;
        }

        warnings.AddRange(Lines(warn.ToString()));
        if (!IsBlank(req.RuleIdFilter))
        {
            var filter = new Regex(req.RuleIdFilter, RegexOptions.CultureInvariant, RegexTimeout);
            docs = docs.Where(d => d.Id is not null && filter.IsMatch(d.Id)).ToList();
        }

        if (docs.Count == 0)
        {
            throw new InvalidOperationException("No rules loaded (check the rule source and rule-id filter).");
        }

        var searcher = new Searcher();
        var hits = new List<SearchHit>();
        foreach (var doc in docs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var options = Options(
                    req,
                    root,
                    registry,
                    null,
                    [root.Path],
                    limit - hits.Count,
                    encoding,
                    utils,
                    ct,
                    configDir
                );
                hits.AddRange(Project(searcher.RunRule(doc, options), root, req, doc.Fix));
            }
            catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
            {
                // One broken rule (unknown language, bad pattern) should not hide the others' hits.
                warnings.Add($"rule '{doc.Id}': {ex.Message}");
            }

            if (hits.Count > limit)
            {
                break;
            }
        }

        return SortHits(hits);
    }

    private static SearchOptions Options(
        SearchRequest req,
        ResolvedRoot root,
        LanguageRegistry registry,
        string? languageId,
        IReadOnlyList<string> paths,
        int remaining,
        string? encoding,
        Dictionary<string, RuleNode>? utils,
        CancellationToken ct,
        string? baseDir = null
    ) =>
        new()
        {
            Registry = registry,
            LanguageId = languageId,
            Paths = paths,
            // Ask for one extra hit so the caller can tell "exactly limit" from "truncated".
            MaxResults = remaining >= int.MaxValue - 1 ? int.MaxValue : Math.Max(1, remaining + 1),
            Encoding = encoding,
            Globs = BuildGlobs(req),
            NoIgnore = req.NoIgnore,
            IncludeHidden = req.IncludeHidden,
            BaseDirectory = baseDir ?? root.BaseDir,
            ContextLines = Math.Max(0, req.ContextLines),
            Utils = utils,
            CancellationToken = ct,
        };

    private LanguageRegistry GetRegistry(string? configPath, string backend, List<string> warnings)
    {
        // Registries are cached because language construction (grammar loading) is the slow part;
        // the key includes the config timestamp so edits to sgconfig.yml are picked up.
        var stamp = configPath is null ? "" : File.GetLastWriteTimeUtc(configPath).Ticks.ToString();
        var key = $"{configPath}|{stamp}|{backend}";
        lock (_gate)
        {
            if (_registry is not null && _registryKey == key)
            {
                return _registry;
            }
        }

        var registry = BuiltinLanguages.CreateRegistry();
        var warn = new StringWriter();
        SgConfigSupport.ApplyAntlrJar(null, configPath);
        var backendName = backend == SearchRequest.DefaultBackend || IsBlank(backend) ? null : backend;
        CustomLanguageBootstrap.ApplyConfig(registry, configPath, backendName, warn);
        warnings.AddRange(Lines(warn.ToString()));
        lock (_gate)
        {
            _registry = registry;
            _registryKey = key;
        }

        return registry;
    }

    private static IEnumerable<ILanguage> AllLanguages(LanguageRegistry registry) =>
        registry.All.DistinctBy(l => l.Id, StringComparer.OrdinalIgnoreCase);

    private static List<SearchHit> Project(
        IEnumerable<MatchResult> matches,
        ResolvedRoot root,
        SearchRequest req,
        string? fix
    ) => matches.Select(m => FromMatch(m, root, req.ContextLines, fix)).ToList();

    private static SearchHit FromMatch(MatchResult m, ResolvedRoot root, int contextLines, string? fix) =>
        new()
        {
            File = m.File,
            DisplayPath = DisplayPath(root, m.File),
            Language = m.Language,
            StartLine = m.StartLine,
            StartColumn = m.StartColumn,
            EndLine = m.EndLine,
            EndColumn = m.EndColumn,
            Start = m.Start,
            End = m.End,
            Text = m.Text,
            // Mirrors Searcher.ToMatch: Lines begins contextLines above the match, clamped to line 1.
            ContextStartLine = Math.Max(1, m.StartLine - Math.Max(0, contextLines)),
            ContextText = m.Lines ?? m.Text,
            MetaVariables = m
                .MetaTexts.Where(kv => kv.Key is not ("" or "_"))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            RuleId = m.RuleId,
            Message = m.Message,
            Severity = m.Severity,
            Encoding = m.Encoding,
            Match = m,
            Fix = fix,
        };

    public static string DisplayPath(ResolvedRoot root, string file)
    {
        var relative = Path.GetRelativePath(root.BaseDir, file);
        return relative.Replace('\\', '/');
    }

    private static List<SearchHit> SortHits(List<SearchHit> hits) =>
        hits.OrderBy(h => h.File, StringComparer.Ordinal).ThenBy(h => h.Start).ToList();

    private static IEnumerable<string> SplitList(string text) =>
        (text ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);
}
