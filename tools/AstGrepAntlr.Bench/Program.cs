using System.CommandLine;
using System.Diagnostics;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Text;

namespace AstGrepAntlr.Bench;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var langOpt = new Option<string>("--lang", "-l") { Required = true };
        var backendOpt = new Option<string>("--backend")
        {
            Description = "tree-sitter or antlr (default: tree-sitter for switchable langs)",
        };
        var patternOpt = new Option<string>("--pattern", "-p") { DefaultValueFactory = _ => "foo($$$ARGS)" };
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore };
        var maxFilesOpt = new Option<int>("--max-files") { DefaultValueFactory = _ => 200 };
        var diagnoseOpt = new Option<bool>("--antlr-diagnose")
        {
            Description = "Report SLL bail sites and per-decision LL cost instead of timing (ANTLR backends only)",
        };
        var topOpt = new Option<int>("--top") { DefaultValueFactory = _ => 15 };
        var treeHashesOpt = new Option<string>("--tree-hashes")
        {
            Description = "Write one tree hash per file to this path (compare before/after grammar changes)",
        };
        var root = new RootCommand("ag4 parse/match micro-benchmark (read / parse / match breakdown)");
        root.Options.Add(langOpt);
        root.Options.Add(backendOpt);
        root.Options.Add(patternOpt);
        root.Options.Add(maxFilesOpt);
        root.Options.Add(diagnoseOpt);
        root.Options.Add(topOpt);
        root.Options.Add(treeHashesOpt);
        root.Arguments.Add(pathsArg);
        root.SetAction(pr =>
        {
            var langId = pr.GetValue(langOpt)!;
            var backendName = pr.GetValue(backendOpt);
            var pattern = pr.GetValue(patternOpt)!;
            var maxFiles = pr.GetValue(maxFilesOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            var registry = BuiltinLanguages.CreateRegistry(LanguageBackend.Antlr);
            if (!string.IsNullOrWhiteSpace(backendName))
            {
                if (!LanguageBackendNames.TryParse(backendName, out var backend))
                {
                    Console.Error.WriteLine($"ag4-bench: unknown backend '{backendName}'");
                    return 2;
                }

                try
                {
                    BuiltinLanguages.ApplyBackend(registry, langId, backend, Console.Error);
                }
                catch (ArgumentException)
                {
                    // non-switchable
                }
            }
            else if (BuiltinLanguages.SwitchableIds.Contains(langId, StringComparer.OrdinalIgnoreCase))
            {
                BuiltinLanguages.ApplyBackend(registry, langId, LanguageBackend.TreeSitter, Console.Error);
            }

            var language = registry.Get(langId);
            var files = EnumerateFiles(paths, language.Extensions).Take(maxFiles).ToList();
            if (files.Count == 0)
            {
                Console.Error.WriteLine("ag4-bench: no files matched");
                return 2;
            }

            var treeHashesPath = pr.GetValue(treeHashesOpt);
            if (!string.IsNullOrWhiteSpace(treeHashesPath))
            {
                AntlrDiagnostics.WriteTreeHashes(language, files, treeHashesPath);
                Console.Error.WriteLine($"ag4-bench: wrote tree hashes for {files.Count} files to {treeHashesPath}");
            }

            if (pr.GetValue(diagnoseOpt))
            {
                if (language is not IAntlrLanguage antlr)
                {
                    Console.Error.WriteLine(
                        $"ag4-bench: --antlr-diagnose needs an ANTLR backend; '{langId}' is not one (try --backend antlr)"
                    );
                    return 2;
                }

                AntlrDiagnostics.Run(antlr, files, pr.GetValue(topOpt), Console.Out);
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(treeHashesPath))
            {
                return 0;
            }

            var compileSw = Stopwatch.StartNew();
            var compiled = new PatternCompiler().Compile(language, new PatternSpec(pattern));
            compileSw.Stop();
            var matcher = new PatternMatcher();

            long readTicks = 0,
                parseTicks = 0,
                matchTicks = 0;
            var nodes = 0L;
            var hits = 0;
            var llFallbackFiles = 0;
            var timings = new ParseTimings();
            var sw = new Stopwatch();

            // Warmup
            foreach (var f in files.Take(3))
            {
                if (SourceFile.TryRead(f, out var src, out _))
                {
                    _ = language.Parse(src!.Text);
                }
            }

            foreach (var file in files)
            {
                sw.Restart();
                if (!SourceFile.TryRead(file, out var source, out _))
                {
                    continue;
                }

                readTicks += sw.ElapsedTicks;

                sw.Restart();
                var parsed = language.Parse(source!.Text, new LanguageParseOptions { Timings = timings });
                parseTicks += sw.ElapsedTicks;
                nodes += parsed.Tree.NodeCount;
                if (parsed.UsedLlFallback)
                {
                    llFallbackFiles++;
                }

                sw.Restart();
                hits += matcher.MatchAll(parsed.Tree, compiled).Count();
                matchTicks += sw.ElapsedTicks;
            }

            var backendLabel = language switch
            {
                TreeSitterLanguage => "tree-sitter",
                IAntlrLanguage => "antlr",
                CSharpLanguage => "roslyn",
                _ => "unknown",
            };

            var readMs = TicksToMs(readTicks);
            var parseMs = TicksToMs(parseTicks);
            var matchMs = TicksToMs(matchTicks);
            var compileMs = compileSw.Elapsed.TotalMilliseconds;
            var total = readMs + parseMs + matchMs;

            Console.WriteLine($"lang={langId} backend={backendLabel} files={files.Count} pattern={pattern}");
            Console.WriteLine(
                $"compile_ms={compileMs:F1} read_ms={readMs:F1} parse_ms={parseMs:F1} match_ms={matchMs:F1} nodes={nodes} hits={hits}"
            );
            if (total > 0)
            {
                Console.WriteLine(
                    $"share read={Pct(readMs, total)}% parse={Pct(parseMs, total)}% match={Pct(matchMs, total)}%"
                );
            }

            Console.WriteLine(
                $"parse_detail lex_ms={timings.Lex.TotalMilliseconds:F1} parse_ms={timings.Parse.TotalMilliseconds:F1} "
                    + $"ll_ms={timings.LlFallback.TotalMilliseconds:F1} build_ms={timings.Build.TotalMilliseconds:F1} "
                    + $"diag_ms={timings.Diagnostics.TotalMilliseconds:F1}"
            );
            Console.WriteLine(
                $"ll_fallback_files={llFallbackFiles}/{files.Count} ({Pct(llFallbackFiles, files.Count)}%)"
            );

            return 0;
        });

        return await root.Parse(args).InvokeAsync();
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static int Pct(double part, double total) => total <= 0 ? 0 : (int)Math.Round(100.0 * part / total);

    private static IEnumerable<string> EnumerateFiles(IEnumerable<string> roots, IReadOnlyList<string> extensions)
    {
        var ext = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                yield return root;
                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (ext.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }
        }
    }
}
