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

var registry = BuiltinLanguages.CreateRegistry();

var root = new RootCommand("ag4 — structural search with ANTLR4 (ast-grep compatible, unofficial)");

var runCmd = new Command("run", "Search with a pattern");
var patternOpt = new Option<string>("--pattern", "-p") { Description = "AST pattern", Required = true };
var langOpt = new Option<string>("--lang", "-l") { Description = "Language id", Required = true };
var jsonOpt = new Option<bool>("--json") { Description = "JSON output" };
var rewriteOpt = new Option<string?>("--rewrite") { Description = "Rewrite template" };
var applyOpt = new Option<bool>("--apply") { Description = "Apply rewrites to files" };
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
runCmd.Arguments.Add(pathsArg);
runCmd.SetAction(parseResult =>
{
    var pattern = parseResult.GetValue(patternOpt) ?? throw new InvalidOperationException("pattern is required");
    var lang = parseResult.GetValue(langOpt) ?? throw new InvalidOperationException("lang is required");
    var json = parseResult.GetValue(jsonOpt);
    var rewrite = parseResult.GetValue(rewriteOpt);
    var apply = parseResult.GetValue(applyOpt);
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
            }
        )
        .ToList();

    if (!string.IsNullOrEmpty(rewrite))
    {
        matches = Rewriter.DeduplicateOverlaps(matches).ToList();
        var byFile = matches.GroupBy(m => m.File);
        foreach (var group in byFile)
        {
            var original = File.ReadAllText(group.Key);
            var edits = group.Select(m => (m, Rewriter.ApplyTemplate(rewrite!, m.MetaVariables, m.Text)));
            var change = Rewriter.PreviewFile(group.Key, original, edits);
            if (apply)
            {
                Rewriter.ApplyAtomic(group.Key, change.NewText, change.OriginalHash);
            }
            else
            {
                Console.Out.Write(change.UnifiedDiff);
            }
        }

        return;
    }

    if (json)
    {
        JsonPrinter.Print(matches, Console.Out);
    }
    else
    {
        TextPrinter.Print(matches, Console.Out);
    }
});

var scanCmd = new Command("scan", "Scan with YAML rules");
var ruleOpt = new Option<string?>("--rule", "-r") { Description = "Rule YAML file" };
var configOpt = new Option<string?>("--config", "-c") { Description = "sgconfig.yml" };
var scanJson = new Option<bool>("--json");
var scanUpdate = new Option<bool>("--update-all") { Description = "Apply rule fixes" };
var scanPaths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };
scanCmd.Options.Add(ruleOpt);
scanCmd.Options.Add(configOpt);
scanCmd.Options.Add(scanJson);
scanCmd.Options.Add(scanUpdate);
scanCmd.Arguments.Add(scanPaths);
scanCmd.SetAction(parseResult =>
{
    var rulePath = parseResult.GetValue(ruleOpt);
    var configPath = parseResult.GetValue(configOpt);
    var json = parseResult.GetValue(scanJson);
    var update = parseResult.GetValue(scanUpdate);
    var paths = parseResult.GetValue(scanPaths) ?? [];
    if (paths.Length == 0)
    {
        paths = ["."];
    }

    CustomLanguageBootstrap.ApplyConfig(registry, configPath);

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
        all.AddRange(searcher.RunRule(doc, new SearchOptions { Registry = registry, Paths = paths }));
        if (update && !string.IsNullOrEmpty(doc.Fix))
        {
            var byFile = all.Where(m => m.RuleId == doc.Id).GroupBy(m => m.File);
            foreach (var group in byFile)
            {
                var original = File.ReadAllText(group.Key);
                var change = Rewriter.PreviewFile(
                    group.Key,
                    original,
                    Rewriter
                        .DeduplicateOverlaps(group)
                        .Select(m => (m, Rewriter.ApplyTemplate(doc.Fix!, m.MetaVariables, m.Text)))
                );
                Rewriter.ApplyAtomic(group.Key, change.NewText, change.OriginalHash);
            }
        }
    }

    if (json)
    {
        JsonPrinter.Print(all, Console.Out);
    }
    else
    {
        TextPrinter.Print(all, Console.Out);
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
    TreeDumper.Write(result.Tree, result.Tree.Root, Console.Out);
    foreach (var d in result.Diagnostics)
    {
        Console.Error.WriteLine($"{(d.IsLexer ? "lexer" : "parser")} {d.Line}:{d.Column} {d.Message}");
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
    Console.WriteLine($"entry: {compiled.EntryRule}");
    TreeDumper.Write(compiled.Tree, compiled.Root, Console.Out);
});

debugCmd.Subcommands.Add(debugParse);
debugCmd.Subcommands.Add(debugPattern);

var mcpCmd = new Command("mcp", "Start MCP server on stdio");
var mcpRoot = new Option<string[]>("--root") { AllowMultipleArgumentsPerToken = true, Description = "Allowed roots" };
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
versionCmd.SetAction(_ => Console.WriteLine("ag4 0.0.0-dev"));

root.Subcommands.Add(runCmd);
root.Subcommands.Add(scanCmd);
root.Subcommands.Add(debugCmd);
root.Subcommands.Add(mcpCmd);
root.Subcommands.Add(versionCmd);

return await root.Parse(args).InvokeAsync();

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
