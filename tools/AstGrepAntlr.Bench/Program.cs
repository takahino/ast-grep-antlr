using System.CommandLine;
using System.Diagnostics;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using AstGrepAntlr.Matching;
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
        var root = new RootCommand("ag4 parse/match micro-benchmark (read / parse / match breakdown)");
        root.Options.Add(langOpt);
        root.Options.Add(backendOpt);
        root.Options.Add(patternOpt);
        root.Options.Add(maxFilesOpt);
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

            var compiled = new PatternCompiler().Compile(language, new PatternSpec(pattern));
            var matcher = new PatternMatcher();

            long readMs = 0,
                parseMs = 0,
                matchMs = 0;
            var nodes = 0L;
            var hits = 0;
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

                readMs += sw.ElapsedMilliseconds;

                sw.Restart();
                var parsed = language.Parse(source!.Text);
                parseMs += sw.ElapsedMilliseconds;
                nodes += parsed.Tree.NodeCount;

                sw.Restart();
                hits += matcher.MatchAll(parsed.Tree, compiled).Count();
                matchMs += sw.ElapsedMilliseconds;
            }

            var backendLabel = language switch
            {
                TreeSitterLanguage => "tree-sitter",
                IAntlrLanguage => "antlr",
                CSharpLanguage => "roslyn",
                _ => "unknown",
            };

            Console.WriteLine($"lang={langId} backend={backendLabel} files={files.Count} pattern={pattern}");
            Console.WriteLine($"read_ms={readMs} parse_ms={parseMs} match_ms={matchMs} nodes={nodes} hits={hits}");
            var total = readMs + parseMs + matchMs;
            if (total > 0)
            {
                Console.WriteLine(
                    $"share read={Pct(readMs, total)}% parse={Pct(parseMs, total)}% match={Pct(matchMs, total)}%"
                );
            }

            return 0;
        });

        return await root.Parse(args).InvokeAsync();
    }

    private static int Pct(long part, long total) => (int)Math.Round(100.0 * part / total);

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
