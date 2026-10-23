namespace AstGrepAntlr.DiffTest;

internal sealed record ToolRunOutcome(bool Succeeded, string StdOut, string StdErr, string JsonMode);

internal static class SearchRunner
{
    public static async Task<ToolRunOutcome> RunAstGrepAsync(
        string astGrepExe,
        string pattern,
        string language,
        string searchPath,
        CancellationToken cancellationToken
    )
    {
        foreach (var jsonMode in new[] { "stream", "pretty" })
        {
            var args = new List<string> { "run", "-p", pattern, "-l", language, $"--json={jsonMode}", searchPath };
            var result = await ProcessRunner.RunAsync(
                astGrepExe,
                args,
                Directory.GetCurrentDirectory(),
                cancellationToken
            );
            if (result.ExitCode == 0 || !string.IsNullOrWhiteSpace(result.StdOut))
            {
                return new ToolRunOutcome(result.ExitCode == 0, result.StdOut, result.StdErr, $"--json={jsonMode}");
            }

            if (!result.StdErr.Contains("--json", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var fallback = await ProcessRunner.RunAsync(
            astGrepExe,
            ["run", "-p", pattern, "-l", language, "--json", searchPath],
            Directory.GetCurrentDirectory(),
            cancellationToken
        );
        return new ToolRunOutcome(fallback.ExitCode == 0, fallback.StdOut, fallback.StdErr, "--json");
    }

    public static async Task<ToolRunOutcome> RunAg4Async(
        string repoRoot,
        string pattern,
        string language,
        string searchPath,
        bool noBuild,
        CancellationToken cancellationToken
    )
    {
        var dotnet = "dotnet";
        var args = new List<string> { "run", "--project", ToolingSetup.Ag4CliProjectPath(repoRoot), "-c", "Release" };
        if (noBuild)
        {
            args.Add("--no-build");
        }

        args.AddRange(["--", "run", "-p", pattern, "-l", language, "--json", searchPath]);
        var result = await ProcessRunner.RunAsync(dotnet, args, repoRoot, cancellationToken);
        return new ToolRunOutcome(
            string.IsNullOrWhiteSpace(result.StdErr) || result.StdOut.Length > 0,
            result.StdOut,
            result.StdErr,
            "--json"
        );
    }
}
