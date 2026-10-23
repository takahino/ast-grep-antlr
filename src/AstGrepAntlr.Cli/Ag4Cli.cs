using System.CommandLine;
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
        var root = BuildRoot(registry, stdout, stderr);
        return await root.Parse(args).InvokeAsync();
    }

    private static RootCommand BuildRoot(LanguageRegistry registry, TextWriter stdout, TextWriter stderr)
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
        runCmd.Arguments.Add(pathsArg);
        runCmd.SetAction(parseResult =>
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
                    }
                )
                .ToList();

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

                return;
            }

            if (json)
            {
                JsonPrinter.Print(matches, stdout);
            }
            else
            {
                TextPrinter.Print(matches, stdout);
            }
        });

        var scanCmd = new Command("scan", "Scan with YAML rules");
        var ruleOpt = new Option<string?>("--rule", "-r") { Description = "Rule YAML file" };
        var configOpt = new Option<string?>("--config", "-c") { Description = "sgconfig.yml" };
        var scanJson = new Option<bool>("--json");
        var scanUpdate = new Option<bool>("--update-all") { Description = "Apply rule fixes" };
        var scanEncoding = new Option<string?>("--encoding")
        {
            Description = "Force file encoding (utf-8, shift_jis, euc-jp, utf-16, ...)",
        };
        var scanPaths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };
        scanCmd.Options.Add(ruleOpt);
        scanCmd.Options.Add(configOpt);
        scanCmd.Options.Add(scanJson);
        scanCmd.Options.Add(scanUpdate);
        scanCmd.Options.Add(scanEncoding);
        scanCmd.Arguments.Add(scanPaths);
        scanCmd.SetAction(parseResult =>
        {
            var rulePath = parseResult.GetValue(ruleOpt);
            var configPath = parseResult.GetValue(configOpt);
            var json = parseResult.GetValue(scanJson);
            var update = parseResult.GetValue(scanUpdate);
            var encoding = parseResult.GetValue(scanEncoding);
            var paths = parseResult.GetValue(scanPaths) ?? [];
            if (paths.Length == 0)
            {
                paths = ["."];
            }

            CustomLanguageBootstrap.ApplyConfig(registry, configPath);
            string? configEncoding = null;
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                configEncoding = RuleLoader.LoadConfig(File.ReadAllText(configPath)).Encoding;
            }

            encoding ??= configEncoding;

            var docs = new List<SgRuleDocument>();
            if (!string.IsNullOrEmpty(rulePath))
            {
                docs.Add(RuleLoader.LoadRule(File.ReadAllText(rulePath!)));
            }

            if (!string.IsNullOrEmpty(configPath))
            {
                var cfg = RuleLoader.LoadConfig(File.ReadAllText(configPath!));
                var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath!)) ?? ".";
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
                            Registry = registry,
                            Paths = paths,
                            Encoding = encoding,
                        }
                    )
                );
                if (update && !string.IsNullOrEmpty(doc.Fix))
                {
                    foreach (var group in all.Where(m => m.RuleId == doc.Id).GroupBy(m => m.File))
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

            if (json)
            {
                JsonPrinter.Print(all, stdout);
            }
            else
            {
                TextPrinter.Print(all, stdout);
            }
        });

        var debugCmd = new Command("debug", "Debug helpers");
        var debugParse = new Command("parse", "Dump syntax tree");
        var debugPattern = new Command("pattern", "Show how a pattern is parsed");
        var debugLang = new Option<string>("--lang", "-l") { Required = true };
        var debugCode = new Option<string?>("--code") { Description = "Code string (else read file arg)" };
        var debugFile = new Argument<string?>("file") { Arity = ArgumentArity.ZeroOrOne };
        debugParse.Options.Add(debugLang);
        debugParse.Options.Add(debugCode);
        debugParse.Arguments.Add(debugFile);
        debugParse.SetAction(pr =>
        {
            var lang = registry.Get(pr.GetValue(debugLang)!);
            var code =
                pr.GetValue(debugCode)
                ?? File.ReadAllText(pr.GetValue(debugFile) ?? throw new ArgumentException("Provide --code or file"));
            var result = new TwoStageParser().Parse(lang, code);
            TreeDumper.Write(result.Tree, result.Tree.Root, stdout);
            foreach (var d in result.Diagnostics)
            {
                stderr.WriteLine($"{(d.IsLexer ? "lexer" : "parser")} {d.Line}:{d.Column} {d.Message}");
            }
        });

        var patOpt = new Option<string>("--pattern", "-p") { Required = true };
        debugPattern.Options.Add(debugLang);
        debugPattern.Options.Add(patOpt);
        debugPattern.SetAction(pr =>
        {
            var lang = registry.Get(pr.GetValue(debugLang)!);
            var pattern = pr.GetValue(patOpt)!;
            var compiled = new PatternCompiler().Compile(lang, new PatternSpec(pattern));
            stdout.WriteLine($"entry: {compiled.EntryRule}");
            TreeDumper.Write(compiled.Tree, compiled.Root, stdout);
        });

        debugCmd.Subcommands.Add(debugParse);
        debugCmd.Subcommands.Add(debugPattern);

        var mcpCmd = new Command("mcp", "Start MCP server on stdio");
        var mcpRoot = new Option<string[]>("--root")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Allowed roots",
        };
        var mcpConfig = new Option<string?>("--config");
        var mcpAllowWrite = new Option<bool>("--allow-write");
        mcpCmd.Options.Add(mcpRoot);
        mcpCmd.Options.Add(mcpConfig);
        mcpCmd.Options.Add(mcpAllowWrite);
        mcpCmd.SetAction(async pr =>
        {
            var roots = pr.GetValue(mcpRoot) ?? [];
            if (roots.Length == 0)
            {
                roots = [Directory.GetCurrentDirectory()];
            }

            var config = pr.GetValue(mcpConfig);
            var allowWrite = pr.GetValue(mcpAllowWrite);
            CustomLanguageBootstrap.ApplyConfig(registry, config);
            await AstGrepAntlr.Mcp.McpHost.RunAsync(registry, roots, allowWrite);
        });

        var versionCmd = new Command("version", "Print version");
        versionCmd.SetAction(_ => stdout.WriteLine("ag4 0.0.0-dev"));

        root.Subcommands.Add(runCmd);
        root.Subcommands.Add(scanCmd);
        root.Subcommands.Add(debugCmd);
        root.Subcommands.Add(mcpCmd);
        root.Subcommands.Add(versionCmd);

        return root;
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

            registry.Register(
                InterpretedLanguage.FromInterpFiles(
                    id,
                    lang.Extensions ?? [],
                    lang.StartRule ?? "program",
                    lang.PatternRules ?? [lang.StartRule ?? "program"],
                    lang.MetaVarPlaceholder ?? "__MV{0}__",
                    lang.IgnoreCase,
                    lexerInterp,
                    parserInterp
                )
            );
        }
    }
}
