using System.Diagnostics;

namespace AstGrepAntlr.DiffTest;

internal sealed record ToolRunOutcome(
    bool Succeeded,
    int ExitCode,
    string StdOut,
    string StdErr,
    string JsonMode,
    long ElapsedMs
);

internal static class SearchRunner
{
    public static TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(120);

    public static async Task<ToolRunOutcome> RunAstGrepAsync(
        string astGrepExe,
        string pattern,
        string language,
        IReadOnlyList<string> searchPaths,
        string? configPath,
        string? rewrite,
        bool apply,
        CancellationToken cancellationToken
    )
    {
        foreach (var jsonMode in new[] { "stream", "pretty" })
        {
            var args = new List<string> { "run", "-p", pattern, "-l", language, $"--json={jsonMode}" };
            AppendConfig(args, configPath);
            AppendRewrite(args, rewrite, apply, updateFlag: "-U");
            args.AddRange(searchPaths);

            var sw = Stopwatch.StartNew();
            var result = await ProcessRunner.RunAsync(
                astGrepExe,
                args,
                Directory.GetCurrentDirectory(),
                ToolTimeout,
                cancellationToken
            );
            sw.Stop();
            if (result.ExitCode is 0 or 1 || !string.IsNullOrWhiteSpace(result.StdOut))
            {
                return new ToolRunOutcome(
                    result.ExitCode is 0 or 1,
                    result.ExitCode,
                    result.StdOut,
                    result.StdErr,
                    $"--json={jsonMode}",
                    sw.ElapsedMilliseconds
                );
            }

            if (!result.StdErr.Contains("--json", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var fallbackArgs = new List<string> { "run", "-p", pattern, "-l", language, "--json" };
        AppendConfig(fallbackArgs, configPath);
        AppendRewrite(fallbackArgs, rewrite, apply, updateFlag: "-U");
        fallbackArgs.AddRange(searchPaths);

        var sw2 = Stopwatch.StartNew();
        var fallback = await ProcessRunner.RunAsync(
            astGrepExe,
            fallbackArgs,
            Directory.GetCurrentDirectory(),
            ToolTimeout,
            cancellationToken
        );
        sw2.Stop();
        return new ToolRunOutcome(
            fallback.ExitCode is 0 or 1,
            fallback.ExitCode,
            fallback.StdOut,
            fallback.StdErr,
            "--json",
            sw2.ElapsedMilliseconds
        );
    }

    public static async Task<ToolRunOutcome> RunAstGrepScanAsync(
        string astGrepExe,
        string ruleFile,
        IReadOnlyList<string> searchPaths,
        string? configPath,
        bool apply,
        CancellationToken cancellationToken
    )
    {
        var args = new List<string> { "scan", "-r", ruleFile, "--json=stream" };
        AppendConfig(args, configPath);
        if (apply)
        {
            args.Add("--update-all");
        }

        args.AddRange(searchPaths);

        var sw = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(
            astGrepExe,
            args,
            Directory.GetCurrentDirectory(),
            timeout: ToolTimeout,
            cancellationToken: cancellationToken
        );
        sw.Stop();
        if (result.ExitCode is 0 or 1 || !string.IsNullOrWhiteSpace(result.StdOut))
        {
            return new ToolRunOutcome(
                result.ExitCode is 0 or 1,
                result.ExitCode,
                result.StdOut,
                result.StdErr,
                "--json=stream",
                sw.ElapsedMilliseconds
            );
        }

        var fallbackArgs = new List<string> { "scan", "-r", ruleFile, "--json" };
        AppendConfig(fallbackArgs, configPath);
        if (apply)
        {
            fallbackArgs.Add("--update-all");
        }

        fallbackArgs.AddRange(searchPaths);
        var sw2 = Stopwatch.StartNew();
        var fallback = await ProcessRunner.RunAsync(
            astGrepExe,
            fallbackArgs,
            Directory.GetCurrentDirectory(),
            ToolTimeout,
            cancellationToken
        );
        sw2.Stop();
        return new ToolRunOutcome(
            fallback.ExitCode is 0 or 1,
            fallback.ExitCode,
            fallback.StdOut,
            fallback.StdErr,
            "--json",
            sw2.ElapsedMilliseconds
        );
    }

    public static async Task<ToolRunOutcome> RunAg4Async(
        string ag4Dll,
        string pattern,
        string language,
        IReadOnlyList<string> searchPaths,
        string? configPath,
        string? rewrite,
        bool apply,
        CancellationToken cancellationToken,
        string? backend = null
    )
    {
        // Use -p=VALUE so patterns starting with '@' (e.g. @Override) are not treated as
        // dotnet response files (a bare argv token `@…` is expanded by the host).
        var args = new List<string> { ag4Dll, "run", "-p=" + pattern, "-l", language, "--json" };
        AppendConfig(args, configPath);
        AppendBackend(args, backend);
        AppendRewrite(args, rewrite, apply, updateFlag: "--apply");
        args.AddRange(searchPaths);

        var sw = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            args,
            Directory.GetCurrentDirectory(),
            ToolTimeout,
            cancellationToken
        );
        sw.Stop();
        return new ToolRunOutcome(
            result.ExitCode is 0 or 1 || result.StdOut.Length > 0,
            result.ExitCode,
            result.StdOut,
            result.StdErr,
            "--json",
            sw.ElapsedMilliseconds
        );
    }

    public static async Task<ToolRunOutcome> RunAg4ScanAsync(
        string ag4Dll,
        string ruleFile,
        IReadOnlyList<string> searchPaths,
        string? configPath,
        bool apply,
        CancellationToken cancellationToken,
        string? backend = null
    )
    {
        var args = new List<string> { ag4Dll, "scan", "-r", ruleFile, "--json" };
        AppendConfig(args, configPath);
        AppendBackend(args, backend);
        if (apply)
        {
            args.Add("--update-all");
        }

        args.AddRange(searchPaths);

        var sw = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            args,
            Directory.GetCurrentDirectory(),
            ToolTimeout,
            cancellationToken
        );
        sw.Stop();
        return new ToolRunOutcome(
            result.ExitCode is 0 or 1 || result.StdOut.Length > 0,
            result.ExitCode,
            result.StdOut,
            result.StdErr,
            "--json",
            sw.ElapsedMilliseconds
        );
    }

    private static void AppendConfig(List<string> args, string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            args.Add("-c");
            args.Add(configPath);
        }
    }

    private static void AppendBackend(List<string> args, string? backend)
    {
        if (!string.IsNullOrWhiteSpace(backend))
        {
            args.Add("--backend");
            args.Add(backend);
        }
    }

    private static void AppendRewrite(List<string> args, string? rewrite, bool apply, string updateFlag)
    {
        if (string.IsNullOrWhiteSpace(rewrite))
        {
            return;
        }

        // Same response-file hazard as -p when rewrite starts with '@'.
        args.Add("--rewrite=" + rewrite);
        if (apply)
        {
            args.Add(updateFlag);
        }
    }
}
