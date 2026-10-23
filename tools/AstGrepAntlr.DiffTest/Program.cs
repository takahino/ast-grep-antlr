using System.CommandLine;

namespace AstGrepAntlr.DiffTest;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootOption = new Option<string?>("--repo-root")
        {
            Description = "Repository root (defaults to parent of AstGrepAntlr.slnx)",
        };
        var corpusOption = new Option<string?>("--corpus") { Description = "Path to corpus.yml" };
        var patternsOption = new Option<string?>("--patterns") { Description = "Path to pattern rules YAML" };
        var skipCloneOption = new Option<bool>("--skip-clone")
        {
            Description = "Reuse existing .difftest/corpus clones",
        };
        var skipNpmOption = new Option<bool>("--skip-npm") { Description = "Skip npm install for ast-grep CLI" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Pass --no-build to dotnet run for ag4" };

        var rootCommand = new RootCommand("ast-grep vs ag4 differential test harness (baseline mode)");
        rootCommand.Options.Add(rootOption);
        rootCommand.Options.Add(corpusOption);
        rootCommand.Options.Add(patternsOption);
        rootCommand.Options.Add(skipCloneOption);
        rootCommand.Options.Add(skipNpmOption);
        rootCommand.Options.Add(noBuildOption);

        rootCommand.SetAction(async parseResult =>
        {
            try
            {
                var repoRoot = parseResult.GetValue(rootOption) ?? RepoPaths.FindRepositoryRoot();
                var corpusPath = parseResult.GetValue(corpusOption) ?? Path.Combine(repoRoot, "difftest", "corpus.yml");
                var patternsPath =
                    parseResult.GetValue(patternsOption) ?? Path.Combine(repoRoot, "difftest", "patterns", "java.yml");

                var report = await DiffTestRunner.RunAsync(
                    new DiffTestOptions
                    {
                        RepoRoot = repoRoot,
                        CorpusPath = corpusPath,
                        PatternsPath = patternsPath,
                        SkipClone = parseResult.GetValue(skipCloneOption),
                        SkipNpm = parseResult.GetValue(skipNpmOption),
                        NoBuild = parseResult.GetValue(noBuildOption),
                    },
                    CancellationToken.None
                );

                var reportDir = Path.Combine(repoRoot, ".difftest", "report", "baseline");
                Console.WriteLine(
                    $"Baseline report written to {reportDir} (same={report.Summary.Same}, only-ast-grep={report.Summary.OnlyAstGrep}, only-ag4={report.Summary.OnlyAg4}, range-diff={report.Summary.RangeDiff}, metavar-diff={report.Summary.MetaVarDiff})."
                );
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"difftest failed: {ex.Message}");
                throw;
            }
        });

        try
        {
            var exit = await rootCommand.Parse(args).InvokeAsync();
            if (exit != 0)
            {
                return exit;
            }
        }
        catch
        {
            return 1;
        }

        return 0;
    }
}
