using AstGrepAntlr.Cli;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

public class CliTests
{
    [Fact]
    public async Task Run_exit_1_when_no_match()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "T.java");
        await File.WriteAllTextAsync(file, "class T { void m() { foo(1); } }");

        var (code, _, _) = await InvokeAsync("run", "-p", "bar($A)", "-l", "java", file);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Run_exit_0_when_match()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "T.java");
        await File.WriteAllTextAsync(file, "class T { void m() { foo(1); } }");

        var (code, _, _) = await InvokeAsync("run", "-p", "foo($A)", "-l", "java", file);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Run_exit_2_on_unknown_language()
    {
        var (code, _, stderr) = await InvokeAsync("run", "-p", "x", "-l", "nosuchlang", ".");
        Assert.Equal(2, code);
        Assert.Contains("ag4:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_exit_2_on_missing_path()
    {
        var (code, _, stderr) = await InvokeAsync("run", "-p", "foo($A)", "-l", "java", "/no/such/path");
        Assert.Equal(2, code);
        Assert.Contains("path does not exist", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_exit_2_when_no_rules()
    {
        using var dir = new TempDir();
        var (code, _, _) = await InvokeAsync("scan", dir.Path);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Scan_exit_1_on_error_severity_match()
    {
        using var dir = new TempDir();
        var java = Path.Combine(dir.Path, "T.java");
        await File.WriteAllTextAsync(java, "class T { void m() { foo(1); } }");
        var rule = Path.Combine(dir.Path, "rule.yml");
        await File.WriteAllTextAsync(
            rule,
            """
            id: test-rule
            language: java
            severity: error
            rule:
              pattern: foo($A)
            """
        );

        var (code, _, _) = await InvokeAsync("scan", "-r", rule, dir.Path);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Version_uses_assembly_informational_version()
    {
        var (code, stdout, _) = await InvokeAsync("version");
        Assert.Equal(0, code);
        Assert.StartsWith("ag4 ", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0-dev hardcoded", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_accepts_strictness_max_results_and_globs()
    {
        using var dir = new TempDir();
        var keep = Path.Combine(dir.Path, "Keep.java");
        var skip = Path.Combine(dir.Path, "Skip.java");
        await File.WriteAllTextAsync(keep, "class Keep { void m() { foo(1); foo(2); } }");
        await File.WriteAllTextAsync(skip, "class Skip { void m() { foo(3); } }");

        var (code, stdout, _) = await InvokeAsync(
            "run",
            "-p",
            "foo($A)",
            "-l",
            "java",
            "--strictness",
            "smart",
            "--max-results",
            "1",
            "--globs",
            "Keep.java",
            dir.Path
        );
        Assert.Equal(0, code);
        Assert.Contains("Keep.java", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Skip.java", stdout, StringComparison.Ordinal);
        // max-results 1: only one match line for foo
        var matchLines = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(l => l.Contains("foo(", StringComparison.Ordinal));
        Assert.Equal(1, matchLines);
    }

    [Fact]
    public async Task Run_with_config_registers_custom_language_lazily()
    {
        var sample = Path.GetFullPath(Path.Combine(Fixtures.RepoRoot, "samples", "dsl"));
        var config = Path.Combine(sample, "sgconfig.yml");
        if (!File.Exists(config))
        {
            return;
        }

        // java search with config present must not fail even if custom langs are listed
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "T.java");
        await File.WriteAllTextAsync(file, "class T { void m() { foo(1); } }");
        var (code, _, stderr) = await InvokeAsync("run", "-p", "foo($A)", "-l", "java", "-c", config, file);
        Assert.Equal(0, code);
        Assert.DoesNotContain("antlr tool failed", stderr, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await Ag4Cli.RunAsync(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
