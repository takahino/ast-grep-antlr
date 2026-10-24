using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Output;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Rules;
using AstGrepAntlr.Search;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Cli;

/// <summary>Entry point for the ag4 CLI, extractable for tests.</summary>
public static class Ag4Cli
{
    public static async Task<int> RunAsync(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var registry = BuiltinLanguages.CreateRegistry();
        var debugOpt = new Option<bool>("--debug") { Description = "Print stack traces on errors" };
        var root = BuildRoot(registry, stdout, stderr, debugOpt);
        root.Options.Add(debugOpt);

        try
        {
            var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
            return await root.Parse(args).InvokeAsync(invocation);
        }
        catch (Exception ex)
        {
            var debug = args.Contains("--debug", StringComparer.Ordinal);
            stderr.WriteLine($"ag4: {ex.Message}");
            if (debug)
            {
                stderr.WriteLine(ex.StackTrace);
            }

            return 2;
        }
    }

    private static int RunHandler(
        System.CommandLine.ParseResult parseResult,
        Option<bool> debugOpt,
        TextWriter stderr,
        Func<int> body
    )
    {
        try
        {
            return body();
        }
        catch (Exception ex)
        {
            var debug = parseResult.GetValue(debugOpt);
            stderr.WriteLine($"ag4: {ex.Message}");
            if (debug)
            {
                stderr.WriteLine(ex.StackTrace);
            }

            return 2;
        }
    }

    private static RootCommand BuildRoot(
        LanguageRegistry registry,
        TextWriter stdout,
        TextWriter stderr,
        Option<bool> debugOpt
    )
    {
        var root = new RootCommand("ag4 — structural search with ANTLR4 (ast-grep compatible, unofficial)");

        var runCmd = new Command("run", "Search with a pattern");
        var patternOpt = new Option<string>("--pattern", "-p") { Description = "AST pattern", Required = true };
        var langOpt = new Option<string>("--lang", "-l") { Description = "Language id", Required = true };
        var jsonOpt = new Option<bool>("--json") { Description = "JSON output" };
        var rewriteOpt = new Option<string?>("--rewrite") { Description = "Rewrite template" };
        var applyOpt = new Option<bool>("--apply") { Description = "Apply rewrites to files" };
        var encodingOpt = new Option<string?>("--encoding")
        {
            Description = "Force file encoding (utf-8, shift_jis, euc-jp, utf-16, ...)",
        };
        var runConfigOpt = CreateConfigOption();
        var runAntlrJarOpt = CreateAntlrJarOption();
        var strictnessOpt = new Option<string>("--strictness")
        {
            Description = "Pattern strictness: cst, smart, ast, relaxed",
            DefaultValueFactory = _ => "smart",
        };
        var maxResultsOpt = new Option<int>("--max-results")
        {
            Description = "Maximum number of matches to return",
            DefaultValueFactory = _ => int.MaxValue,
        };
        var globsOpt = new Option<string[]>("--globs")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Include globs (repeatable; prefix ! to exclude)",
            DefaultValueFactory = _ => [],
        };
        var contextOpt = new Option<int>("--context", "-C")
        {
            Description = "Lines of context around each match",
            DefaultValueFactory = _ => 0,
        };
        var showErrorsOpt = new Option<bool>("--show-errors") { Description = "Print file read/parse errors" };
        var runNoIgnoreOpt = CreateNoIgnoreOption();
        var pathsArg = new Argument<string[]>("paths")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Files or directories",
        };
        runCmd.Options.Add(patternOpt);
        runCmd.Options.Add(langOpt);
        runCmd.Options.Add(jsonOpt);
        runCmd.Options.Add(rewriteOpt);
        runCmd.Options.Add(applyOpt);
        runCmd.Options.Add(encodingOpt);
        runCmd.Options.Add(runConfigOpt);
        runCmd.Options.Add(runAntlrJarOpt);
        runCmd.Options.Add(strictnessOpt);
        runCmd.Options.Add(maxResultsOpt);
        runCmd.Options.Add(globsOpt);
        runCmd.Options.Add(contextOpt);
        runCmd.Options.Add(showErrorsOpt);
        runCmd.Options.Add(runNoIgnoreOpt);
        runCmd.Arguments.Add(pathsArg);
        runCmd.SetAction(parseResult =>
            RunHandler(
                parseResult,
                debugOpt,
                stderr,
                () =>
                {
                    var pattern =
                        parseResult.GetValue(patternOpt) ?? throw new InvalidOperationException("pattern is required");
                    var lang = parseResult.GetValue(langOpt) ?? throw new InvalidOperationException("lang is required");
                    var json = parseResult.GetValue(jsonOpt);
                    var rewrite = parseResult.GetValue(rewriteOpt);
                    var apply = parseResult.GetValue(applyOpt);
                    var encoding = parseResult.GetValue(encodingOpt);
                    var paths = parseResult.GetValue(pathsArg) ?? [];
                    if (paths.Length == 0)
                    {
                        paths = ["."];
                    }

                    var configPath = ResolveConfig(parseResult.GetValue(runConfigOpt), paths);
                    ApplyAntlrJar(parseResult.GetValue(runAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(registry, configPath);
                    encoding ??= LoadConfigEncoding(configPath);

                    var strictnessName = parseResult.GetValue(strictnessOpt) ?? "smart";
                    var strictness = Enum.TryParse<Strictness>(strictnessName, ignoreCase: true, out var st)
                        ? st
                        : throw new InvalidOperationException($"Unknown strictness '{strictnessName}'.");

                    var missing = new List<string>();
                    var searcher = new Searcher();
                    var matches = searcher
                        .RunPattern(
                            pattern,
                            new SearchOptions
                            {
                                Registry = registry,
                                LanguageId = lang,
                                Paths = paths,
                                Encoding = encoding,
                                MissingRoots = missing,
                                MaxResults = parseResult.GetValue(maxResultsOpt),
                                Globs = parseResult.GetValue(globsOpt),
                                ContextLines = parseResult.GetValue(contextOpt),
                                ShowErrors = parseResult.GetValue(showErrorsOpt),
                                NoIgnore = parseResult.GetValue(runNoIgnoreOpt),
                                BaseDirectory = ConfigBaseDir(configPath),
                            },
                            strictness
                        )
                        .ToList();

                    foreach (var m in missing)
                    {
                        stderr.WriteLine($"ag4: warning: path does not exist: {m}");
                    }

                    if (!string.IsNullOrEmpty(rewrite))
                    {
                        foreach (var group in matches.GroupBy(m => m.File))
                        {
                            var change = Rewriter.RewriteFile(
                                group.Key,
                                group,
                                m => Rewriter.ApplyTemplate(rewrite!, m.MetaTexts, m.Text),
                                apply,
                                encoding
                            );
                            if (!apply)
                            {
                                stdout.Write(change.UnifiedDiff);
                            }
                        }

                        return missing.Count > 0 ? 2
                            : matches.Count > 0 ? 0
                            : 1;
                    }

                    if (json)
                    {
                        JsonPrinter.Print(matches, stdout);
                    }
                    else
                    {
                        TextPrinter.Print(matches, stdout);
                    }

                    if (missing.Count > 0)
                    {
                        return 2;
                    }

                    return matches.Count > 0 ? 0 : 1;
                }
            )
        );

        var scanCmd = new Command("scan", "Scan with YAML rules");
        var ruleOpt = new Option<string?>("--rule", "-r") { Description = "Rule YAML file" };
        var configOpt = CreateConfigOption();
        var scanJson = new Option<bool>("--json");
        var scanUpdate = new Option<bool>("--update-all") { Description = "Apply rule fixes" };
        var scanEncoding = new Option<string?>("--encoding")
        {
            Description = "Force file encoding (utf-8, shift_jis, euc-jp, utf-16, ...)",
        };
        var errorOnOpt = new Option<string>("--error-on")
        {
            Description = "Exit 1 when a match has this severity or higher",
            DefaultValueFactory = _ => "error",
        };
        var scanAntlrJarOpt = CreateAntlrJarOption();
        var scanNoIgnoreOpt = CreateNoIgnoreOption();
        var scanPaths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };
        scanCmd.Options.Add(ruleOpt);
        scanCmd.Options.Add(configOpt);
        scanCmd.Options.Add(scanJson);
        scanCmd.Options.Add(scanUpdate);
        scanCmd.Options.Add(scanEncoding);
        scanCmd.Options.Add(errorOnOpt);
        scanCmd.Options.Add(scanAntlrJarOpt);
        scanCmd.Options.Add(scanNoIgnoreOpt);
        scanCmd.Arguments.Add(scanPaths);
        scanCmd.SetAction(parseResult =>
            RunHandler(
                parseResult,
                debugOpt,
                stderr,
                () =>
                {
                    var rulePath = parseResult.GetValue(ruleOpt);
                    var configPath = parseResult.GetValue(configOpt);
                    var json = parseResult.GetValue(scanJson);
                    var update = parseResult.GetValue(scanUpdate);
                    var encoding = parseResult.GetValue(scanEncoding);
                    var errorOn = parseResult.GetValue(errorOnOpt) ?? "error";
                    var paths = parseResult.GetValue(scanPaths) ?? [];
                    if (paths.Length == 0)
                    {
                        paths = ["."];
                    }

                    var effectiveConfig = configPath;
                    if (string.IsNullOrEmpty(effectiveConfig))
                    {
                        effectiveConfig = SgConfigLocator.FindUpwards(ResolveConfigSearchRoot(paths));
                    }

                    ApplyAntlrJar(parseResult.GetValue(scanAntlrJarOpt), effectiveConfig);
                    CustomLanguageBootstrap.ApplyConfig(registry, effectiveConfig);
                    encoding ??= LoadConfigEncoding(effectiveConfig);

                    var docs = new List<SgRuleDocument>();
                    Dictionary<string, RuleNode>? utilMap = null;
                    string? baseDir = null;
                    if (!string.IsNullOrEmpty(effectiveConfig) && File.Exists(effectiveConfig))
                    {
                        var cfg = RuleLoader.LoadConfig(File.ReadAllText(effectiveConfig!));
                        baseDir = Path.GetDirectoryName(Path.GetFullPath(effectiveConfig!)) ?? ".";
                        utilMap = LoadUtilDirs(cfg, baseDir);
                    }

                    if (!string.IsNullOrEmpty(rulePath))
                    {
                        docs.Add(EnsureRuleId(RuleLoader.LoadRule(File.ReadAllText(rulePath!)), rulePath!, stderr));
                    }
                    else if (!string.IsNullOrEmpty(effectiveConfig) && File.Exists(effectiveConfig))
                    {
                        var cfg = RuleLoader.LoadConfig(File.ReadAllText(effectiveConfig!));
                        baseDir ??= Path.GetDirectoryName(Path.GetFullPath(effectiveConfig!)) ?? ".";
                        foreach (var dir in cfg.RuleDirs ?? [])
                        {
                            foreach (var loaded in RuleLoader.LoadRuleDir(Path.Combine(baseDir, dir)))
                            {
                                docs.Add(EnsureRuleId(loaded.Document, loaded.Path, stderr));
                            }
                        }
                    }

                    if (docs.Count == 0)
                    {
                        stderr.WriteLine("ag4: no rules loaded (use -r/--rule, -c/--config, or sgconfig.yml)");
                        return 2;
                    }

                    var missing = new List<string>();
                    var searcher = new Searcher();
                    var all = new List<MatchResult>();
                    var noIgnore = parseResult.GetValue(scanNoIgnoreOpt);
                    foreach (var doc in docs)
                    {
                        // B12: keep per-rule hits so fix only applies to that rule's matches.
                        var hits = searcher
                            .RunRule(
                                doc,
                                new SearchOptions
                                {
                                    Registry = registry,
                                    Paths = paths,
                                    Encoding = encoding,
                                    MissingRoots = missing,
                                    Utils = utilMap,
                                    NoIgnore = noIgnore,
                                    BaseDirectory = baseDir ?? ConfigBaseDir(effectiveConfig),
                                }
                            )
                            .ToList();
                        all.AddRange(hits);
                        if (update && !string.IsNullOrEmpty(doc.Fix))
                        {
                            foreach (var group in hits.GroupBy(m => m.File))
                            {
                                Rewriter.RewriteFile(
                                    group.Key,
                                    group,
                                    m => Rewriter.ApplyTemplate(doc.Fix!, m.MetaTexts, m.Text),
                                    apply: true,
                                    encoding
                                );
                            }
                        }
                    }

                    foreach (var m in missing)
                    {
                        stderr.WriteLine($"ag4: warning: path does not exist: {m}");
                    }

                    if (json)
                    {
                        JsonPrinter.Print(all, stdout);
                    }
                    else
                    {
                        TextPrinter.Print(all, stdout);
                    }

                    if (missing.Count > 0)
                    {
                        return 2;
                    }

                    return all.Any(m => SeverityMeetsThreshold(m.Severity, errorOn)) ? 1 : 0;
                }
            )
        );

        var debugCmd = new Command("debug", "Debug helpers");
        var debugParse = new Command("parse", "Dump syntax tree");
        var debugPattern = new Command("pattern", "Show how a pattern is parsed");
        var debugLang = new Option<string>("--lang", "-l") { Required = true };
        var debugCode = new Option<string?>("--code") { Description = "Code string (else read file arg)" };
        var debugConfigOpt = CreateConfigOption();
        var debugAntlrJarOpt = CreateAntlrJarOption();
        var debugFile = new Argument<string?>("file") { Arity = ArgumentArity.ZeroOrOne };
        debugParse.Options.Add(debugLang);
        debugParse.Options.Add(debugCode);
        debugParse.Options.Add(debugConfigOpt);
        debugParse.Options.Add(debugAntlrJarOpt);
        debugParse.Arguments.Add(debugFile);
        debugParse.SetAction(pr =>
            RunHandler(
                pr,
                debugOpt,
                stderr,
                () =>
                {
                    var paths = pr.GetValue(debugFile) is { } f ? new[] { f } : Array.Empty<string>();
                    var configPath = ResolveConfig(pr.GetValue(debugConfigOpt), paths);
                    ApplyAntlrJar(pr.GetValue(debugAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(registry, configPath);
                    var lang = registry.Get(pr.GetValue(debugLang)!);
                    var code =
                        pr.GetValue(debugCode)
                        ?? File.ReadAllText(
                            pr.GetValue(debugFile) ?? throw new ArgumentException("Provide --code or file")
                        );
                    var result = new TwoStageParser().Parse(lang, code);
                    TreeDumper.Write(result.Tree, result.Tree.Root, stdout);
                    foreach (var d in result.Diagnostics)
                    {
                        stderr.WriteLine($"{(d.IsLexer ? "lexer" : "parser")} {d.Line}:{d.Column} {d.Message}");
                    }

                    return 0;
                }
            )
        );

        var patOpt = new Option<string>("--pattern", "-p") { Required = true };
        var debugPatLang = new Option<string>("--lang", "-l") { Required = true };
        var debugPatConfigOpt = CreateConfigOption();
        var debugPatAntlrJarOpt = CreateAntlrJarOption();
        debugPattern.Options.Add(debugPatLang);
        debugPattern.Options.Add(patOpt);
        debugPattern.Options.Add(debugPatConfigOpt);
        debugPattern.Options.Add(debugPatAntlrJarOpt);
        debugPattern.SetAction(pr =>
            RunHandler(
                pr,
                debugOpt,
                stderr,
                () =>
                {
                    var configPath = ResolveConfig(pr.GetValue(debugPatConfigOpt), []);
                    ApplyAntlrJar(pr.GetValue(debugPatAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(registry, configPath);
                    var lang = registry.Get(pr.GetValue(debugPatLang)!);
                    var pattern = pr.GetValue(patOpt)!;
                    var compiled = new PatternCompiler().Compile(lang, new PatternSpec(pattern));
                    stdout.WriteLine($"entry: {compiled.EntryRule}");
                    TreeDumper.Write(compiled.Tree, compiled.Root, stdout);
                    return 0;
                }
            )
        );

        debugCmd.Subcommands.Add(debugParse);
        debugCmd.Subcommands.Add(debugPattern);

        var mcpCmd = new Command("mcp", "Start MCP server on stdio");
        var mcpRoot = new Option<string[]>("--root")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Allowed roots",
        };
        var mcpConfig = CreateConfigOption();
        var mcpAntlrJarOpt = CreateAntlrJarOption();
        var mcpAllowWrite = new Option<bool>("--allow-write");
        mcpCmd.Options.Add(mcpRoot);
        mcpCmd.Options.Add(mcpConfig);
        mcpCmd.Options.Add(mcpAntlrJarOpt);
        mcpCmd.Options.Add(mcpAllowWrite);
        mcpCmd.SetAction(async pr =>
        {
            try
            {
                var roots = pr.GetValue(mcpRoot) ?? [];
                if (roots.Length == 0)
                {
                    roots = [Directory.GetCurrentDirectory()];
                }

                var config = pr.GetValue(mcpConfig) ?? SgConfigLocator.FindUpwards();
                ApplyAntlrJar(pr.GetValue(mcpAntlrJarOpt), config);
                var allowWrite = pr.GetValue(mcpAllowWrite);
                CustomLanguageBootstrap.ApplyConfig(registry, config);
                await AstGrepAntlr.Mcp.McpHost.RunAsync(registry, roots, allowWrite);
                return 0;
            }
            catch (Exception ex)
            {
                var debug = pr.GetValue(debugOpt);
                stderr.WriteLine($"ag4: {ex.Message}");
                if (debug)
                {
                    stderr.WriteLine(ex.StackTrace);
                }

                return 2;
            }
        });

        var versionCmd = new Command("version", "Print version");
        versionCmd.SetAction(_ =>
        {
            var ver =
                typeof(Ag4Cli)
                    .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? typeof(Ag4Cli).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            stdout.WriteLine($"ag4 {ver}");
            return 0;
        });

        root.Subcommands.Add(runCmd);
        root.Subcommands.Add(scanCmd);
        root.Subcommands.Add(debugCmd);
        root.Subcommands.Add(mcpCmd);
        root.Subcommands.Add(versionCmd);

        return root;
    }

    private static Option<string?> CreateConfigOption() =>
        new("--config", "-c") { Description = "sgconfig.yml (auto-detected when omitted)" };

    private static Option<string?> CreateAntlrJarOption() =>
        new("--antlr-jar") { Description = "Path to antlr-*-complete.jar" };

    private static Option<bool> CreateNoIgnoreOption() =>
        new("--no-ignore") { Description = "Do not respect .gitignore or default skip dirs (still skips .git)" };

    private static string? ResolveConfig(string? explicitConfig, string[] paths)
    {
        if (!string.IsNullOrEmpty(explicitConfig))
        {
            return explicitConfig;
        }

        return SgConfigLocator.FindUpwards(ResolveConfigSearchRoot(paths));
    }

    private static void ApplyAntlrJar(string? antlrJar, string? configPath)
    {
        AntlrJarLocator.ExplicitPath = antlrJar;
        AntlrJarLocator.ConfigDirectory = ConfigBaseDir(configPath);
    }

    private static string? ConfigBaseDir(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(configPath));
    }

    private static string? LoadConfigEncoding(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        return RuleLoader.LoadConfig(File.ReadAllText(configPath)).Encoding;
    }

    private static Dictionary<string, RuleNode>? LoadUtilDirs(SgConfig cfg, string baseDir)
    {
        if (cfg.UtilDirs is null || cfg.UtilDirs.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, RuleNode>(StringComparer.Ordinal);
        foreach (var dir in cfg.UtilDirs)
        {
            var full = Path.IsPathRooted(dir) ? dir : Path.Combine(baseDir, dir);
            foreach (var (doc, _) in RuleLoader.LoadRuleDir(full))
            {
                if (doc.Utils is not null)
                {
                    foreach (var (name, rule) in doc.Utils)
                    {
                        map[name] = rule;
                    }
                }

                // A util file may itself be a named rule referenced via matches: id
                if (!string.IsNullOrEmpty(doc.Id) && doc.Rule is not null)
                {
                    map[doc.Id] = doc.Rule;
                }
            }
        }

        return map.Count == 0 ? null : map;
    }

    private static bool SeverityMeetsThreshold(string? severity, string errorOn)
    {
        var rank = SeverityRank(severity ?? "warning");
        var threshold = SeverityRank(errorOn);
        return rank >= threshold;
    }

    private static SgRuleDocument EnsureRuleId(SgRuleDocument doc, string path, TextWriter stderr)
    {
        if (!string.IsNullOrEmpty(doc.Id))
        {
            return doc;
        }

        var synthetic = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(synthetic))
        {
            synthetic = "unnamed-rule";
        }

        stderr.WriteLine($"ag4: warning: rule missing id; using '{synthetic}' from {path}");
        doc.Id = synthetic;
        return doc;
    }

    private static string ResolveConfigSearchRoot(string[] paths)
    {
        var anchor = paths.Length > 0 ? paths[0] : ".";
        if (File.Exists(anchor))
        {
            return Path.GetDirectoryName(Path.GetFullPath(anchor)) ?? ".";
        }

        return Directory.Exists(anchor) ? Path.GetFullPath(anchor) : Directory.GetCurrentDirectory();
    }

    private static int SeverityRank(string severity) =>
        severity.ToLowerInvariant() switch
        {
            "hint" => 0,
            "info" => 1,
            "warning" => 2,
            "error" => 3,
            _ => 2,
        };
}

internal static class SgConfigLocator
{
    public static string? FindUpwards(string? startDirectory = null)
    {
        var dir = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        while (true)
        {
            var candidate = Path.Combine(dir, "sgconfig.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}

internal static class CustomLanguageBootstrap
{
    public static void ApplyConfig(LanguageRegistry registry, string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            return;
        }

        var cfg = RuleLoader.LoadConfig(File.ReadAllText(configPath));
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        if (cfg.CustomLanguages is null)
        {
            return;
        }

        foreach (var (id, lang) in cfg.CustomLanguages)
        {
            if (lang.Grammar is null || lang.Grammar.Count == 0)
            {
                continue;
            }

            var files = lang.Grammar.Select(g => Path.IsPathRooted(g) ? g : Path.Combine(baseDir, g)).ToList();
            var extensions = lang.Extensions ?? [];
            var startRule = lang.StartRule ?? "program";
            var patternRules = lang.PatternRules ?? [startRule];
            var placeholder = lang.MetaVarPlaceholder ?? "__MV{0}__";
            var ignoreCase = lang.IgnoreCase;
            var variants = lang.MetaVarVariants;
            var langId = id;

            registry.RegisterLazy(
                langId,
                () =>
                {
                    string lexerInterp;
                    string parserInterp;
                    if (files.All(f => f.EndsWith(".interp", StringComparison.OrdinalIgnoreCase)))
                    {
                        lexerInterp = files.First(f => f.Contains("Lexer", StringComparison.OrdinalIgnoreCase));
                        parserInterp = files.First(f => f.Contains("Parser", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        (lexerInterp, parserInterp) = GrammarInterpCache.EnsureInterp(files);
                    }

                    return InterpretedLanguage.FromInterpFiles(
                        langId,
                        extensions,
                        startRule,
                        patternRules,
                        placeholder,
                        ignoreCase,
                        lexerInterp,
                        parserInterp,
                        variants
                    );
                }
            );
        }
    }
}
