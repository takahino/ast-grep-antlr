using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.Config;
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
        var root = new RootCommand(
            "ag4 — structural search (tree-sitter / ANTLR / Roslyn; ast-grep compatible, unofficial)"
        );

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
        var runBackendOpt = CreateBackendOption();
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
            // One value per --globs occurrence so the paths argument is not swallowed.
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore,
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
        runCmd.Options.Add(runBackendOpt);
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
                    SgConfigSupport.ApplyAntlrJar(parseResult.GetValue(runAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(
                        registry,
                        configPath,
                        parseResult.GetValue(runBackendOpt),
                        stderr
                    );
                    encoding ??= SgConfigSupport.LoadConfigEncoding(configPath);

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
                                BaseDirectory = SgConfigSupport.ConfigBaseDir(configPath),
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
        var scanBackendOpt = CreateBackendOption();
        var scanNoIgnoreOpt = CreateNoIgnoreOption();
        var scanPaths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };
        scanCmd.Options.Add(ruleOpt);
        scanCmd.Options.Add(configOpt);
        scanCmd.Options.Add(scanJson);
        scanCmd.Options.Add(scanUpdate);
        scanCmd.Options.Add(scanEncoding);
        scanCmd.Options.Add(errorOnOpt);
        scanCmd.Options.Add(scanAntlrJarOpt);
        scanCmd.Options.Add(scanBackendOpt);
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
                        effectiveConfig = SgConfigLocator.FindUpwards(SgConfigSupport.ResolveConfigSearchRoot(paths));
                    }

                    SgConfigSupport.ApplyAntlrJar(parseResult.GetValue(scanAntlrJarOpt), effectiveConfig);
                    CustomLanguageBootstrap.ApplyConfig(
                        registry,
                        effectiveConfig,
                        parseResult.GetValue(scanBackendOpt),
                        stderr
                    );
                    encoding ??= SgConfigSupport.LoadConfigEncoding(effectiveConfig);

                    var docs = new List<SgRuleDocument>();
                    Dictionary<string, RuleNode>? utilMap = null;
                    string? baseDir = null;
                    if (!string.IsNullOrEmpty(effectiveConfig) && File.Exists(effectiveConfig))
                    {
                        var cfg = RuleLoader.LoadConfig(File.ReadAllText(effectiveConfig!));
                        baseDir = Path.GetDirectoryName(Path.GetFullPath(effectiveConfig!)) ?? ".";
                        utilMap = SgConfigSupport.LoadUtilDirs(cfg, baseDir);
                    }

                    if (!string.IsNullOrEmpty(rulePath))
                    {
                        docs.Add(
                            SgConfigSupport.EnsureRuleId(
                                RuleLoader.LoadRule(File.ReadAllText(rulePath!)),
                                rulePath!,
                                stderr
                            )
                        );
                    }
                    else if (!string.IsNullOrEmpty(effectiveConfig) && File.Exists(effectiveConfig))
                    {
                        var cfg = RuleLoader.LoadConfig(File.ReadAllText(effectiveConfig!));
                        baseDir ??= Path.GetDirectoryName(Path.GetFullPath(effectiveConfig!)) ?? ".";
                        foreach (var dir in cfg.RuleDirs ?? [])
                        {
                            foreach (var loaded in RuleLoader.LoadRuleDir(Path.Combine(baseDir, dir)))
                            {
                                docs.Add(SgConfigSupport.EnsureRuleId(loaded.Document, loaded.Path, stderr));
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
                                    BaseDirectory = baseDir ?? SgConfigSupport.ConfigBaseDir(effectiveConfig),
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
        var debugBackendOpt = CreateBackendOption();
        var debugFile = new Argument<string?>("file") { Arity = ArgumentArity.ZeroOrOne };
        debugParse.Options.Add(debugLang);
        debugParse.Options.Add(debugCode);
        debugParse.Options.Add(debugConfigOpt);
        debugParse.Options.Add(debugAntlrJarOpt);
        debugParse.Options.Add(debugBackendOpt);
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
                    SgConfigSupport.ApplyAntlrJar(pr.GetValue(debugAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(registry, configPath, pr.GetValue(debugBackendOpt), stderr);
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
        var debugPatBackendOpt = CreateBackendOption();
        debugPattern.Options.Add(debugPatLang);
        debugPattern.Options.Add(patOpt);
        debugPattern.Options.Add(debugPatConfigOpt);
        debugPattern.Options.Add(debugPatAntlrJarOpt);
        debugPattern.Options.Add(debugPatBackendOpt);
        debugPattern.SetAction(pr =>
            RunHandler(
                pr,
                debugOpt,
                stderr,
                () =>
                {
                    var configPath = ResolveConfig(pr.GetValue(debugPatConfigOpt), []);
                    SgConfigSupport.ApplyAntlrJar(pr.GetValue(debugPatAntlrJarOpt), configPath);
                    CustomLanguageBootstrap.ApplyConfig(registry, configPath, pr.GetValue(debugPatBackendOpt), stderr);
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
        var mcpBackendOpt = CreateBackendOption();
        var mcpAllowWrite = new Option<bool>("--allow-write");
        mcpCmd.Options.Add(mcpRoot);
        mcpCmd.Options.Add(mcpConfig);
        mcpCmd.Options.Add(mcpAntlrJarOpt);
        mcpCmd.Options.Add(mcpBackendOpt);
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
                SgConfigSupport.ApplyAntlrJar(pr.GetValue(mcpAntlrJarOpt), config);
                var allowWrite = pr.GetValue(mcpAllowWrite);
                CustomLanguageBootstrap.ApplyConfig(registry, config, pr.GetValue(mcpBackendOpt), stderr);
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

    private static Option<string?> CreateBackendOption() =>
        new("--backend")
        {
            Description =
                "Parser backend for switchable languages: tree-sitter (default), antlr (java/cpp/python), or roslyn (csharp)",
        };

    private static Option<bool> CreateNoIgnoreOption() =>
        new("--no-ignore") { Description = "Do not respect .gitignore or default skip dirs (still skips .git)" };

    private static string? ResolveConfig(string? explicitConfig, string[] paths)
    {
        if (!string.IsNullOrEmpty(explicitConfig))
        {
            return explicitConfig;
        }

        return SgConfigLocator.FindUpwards(SgConfigSupport.ResolveConfigSearchRoot(paths));
    }

    private static bool SeverityMeetsThreshold(string? severity, string errorOn)
    {
        var rank = SeverityRank(severity ?? "warning");
        var threshold = SeverityRank(errorOn);
        return rank >= threshold;
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
