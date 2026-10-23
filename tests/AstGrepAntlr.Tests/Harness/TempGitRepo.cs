using System.Diagnostics;

namespace AstGrepAntlr.Tests.Harness;

/// <summary>Temporary git repository for gitignore oracle tests.</summary>
public sealed class TempGitRepo : IDisposable
{
    public TempDir Dir { get; } = new("ag4-git");

    public string Path => Dir.Path;

    public static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public TempGitRepo Init()
    {
        Run("init");
        Run("config user.email ag4@test.local");
        Run("config user.name ag4-test");
        return this;
    }

    public void WriteGitignore(string content, string relativeDir = ".")
    {
        var path = relativeDir == "." ? "gitignore-root" : relativeDir;
        _ = path;
        Dir.WriteText(relativeDir == "." ? ".gitignore" : System.IO.Path.Combine(relativeDir, ".gitignore"), content);
    }

    public bool CheckIgnore(string relativePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"check-ignore -q -- \"{relativePath.Replace('\\', '/')}\"",
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git not started");
        p.WaitForExit(10000);
        return p.ExitCode == 0;
    }

    public string Run(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git not started");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        if (p.ExitCode != 0 && !args.StartsWith("check-ignore", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"git {args} failed ({p.ExitCode}): {stderr}");
        }

        return stdout;
    }

    public void Dispose() => Dir.Dispose();
}
