using System.Text.Json;

namespace AstGrepAntlr.DiffTest;

internal static class ToolingSetup
{
    public const string AstGrepVersion = "0.51.0";

    private static readonly JsonSerializerOptions PackageJsonOptions = new() { WriteIndented = true };

    public static async Task<string> EnsureAstGrepAsync(
        string toolsDir,
        bool skipNpm,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(toolsDir);
        var packageJsonPath = Path.Combine(toolsDir, "package.json");
        var expected = new Dictionary<string, object>
        {
            ["name"] = "ast-grep-antlr-difftest-tools",
            ["private"] = true,
            ["dependencies"] = new Dictionary<string, string> { ["@ast-grep/cli"] = AstGrepVersion },
        };

        var needsInstall = true;
        if (File.Exists(packageJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
                if (
                    doc.RootElement.TryGetProperty("dependencies", out var deps)
                    && deps.TryGetProperty("@ast-grep/cli", out var version)
                    && version.GetString() == AstGrepVersion
                    && File.Exists(ResolveAstGrepExecutable(toolsDir))
                )
                {
                    needsInstall = false;
                }
            }
            catch (JsonException)
            {
                needsInstall = true;
            }
        }

        if (!skipNpm && needsInstall)
        {
            await File.WriteAllTextAsync(
                packageJsonPath,
                JsonSerializer.Serialize(expected, PackageJsonOptions),
                cancellationToken
            );

            var npm = FindOnPath("npm") ?? "npm";
            var result = await ProcessRunner.RunAsync(
                npm,
                ["install", "--no-fund", "--no-audit"],
                toolsDir,
                cancellationToken
            );
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"npm install failed in {toolsDir}:{Environment.NewLine}{result.StdErr}{result.StdOut}"
                );
            }
        }

        var executable = ResolveAstGrepExecutable(toolsDir);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"ast-grep executable not found at {executable}. Run without --skip-npm or install Node.js/npm."
            );
        }

        return executable;
    }

    public static string ResolveAstGrepExecutable(string toolsDir)
    {
        var baseDir = Path.Combine(toolsDir, "node_modules", "@ast-grep", "cli");
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(baseDir, "ast-grep.exe");
        }

        var unix = Path.Combine(baseDir, "ast-grep");
        return File.Exists(unix) ? unix : Path.Combine(baseDir, "sg");
    }

    public static string Ag4CliProjectPath(string repoRoot) =>
        Path.Combine(repoRoot, "src", "AstGrepAntlr.Cli", "AstGrepAntlr.Cli.csproj");

    private static string? FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [string.Empty];

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
