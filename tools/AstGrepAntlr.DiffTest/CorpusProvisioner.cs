namespace AstGrepAntlr.DiffTest;

internal static class CorpusProvisioner
{
    public static async Task<List<PreparedRepo>> EnsureCorpusAsync(
        CorpusConfig corpus,
        string corpusRoot,
        bool skipClone,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(corpusRoot);
        var git = FindGit() ?? "git";
        var prepared = new List<PreparedRepo>();

        foreach (var repo in corpus.Repos)
        {
            var localPath = Path.Combine(corpusRoot, repo.Id);
            if (!skipClone)
            {
                await EnsureCloneAsync(git, repo, localPath, cancellationToken);
            }
            else if (!Directory.Exists(localPath))
            {
                throw new DirectoryNotFoundException(
                    $"Corpus directory missing for {repo.Id}: {localPath} (re-run without --skip-clone)."
                );
            }

            var rev = await ProcessRunner.RunAsync(git, ["rev-parse", "HEAD"], localPath, cancellationToken);
            if (rev.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git rev-parse failed for {repo.Id}:{Environment.NewLine}{rev.StdErr}"
                );
            }

            var sha = rev.StdOut.Trim();
            if (
                !string.IsNullOrWhiteSpace(repo.Sha)
                && !string.Equals(repo.Sha, sha, StringComparison.OrdinalIgnoreCase)
            )
            {
                // Shallow clones of tags can still resolve; warn via stderr but continue with actual SHA.
                Console.Error.WriteLine($"difftest: warning: {repo.Id} HEAD {sha} differs from pinned sha {repo.Sha}");
            }

            var language = string.IsNullOrWhiteSpace(repo.Language) ? corpus.Language : repo.Language;
            var searchPaths = ResolveSearchPaths(localPath, repo.Paths);
            prepared.Add(new PreparedRepo(repo.Id, localPath, repo.Ref, sha, language, searchPaths));
        }

        return prepared;
    }

    private static List<string> ResolveSearchPaths(string localPath, List<string> relativePaths)
    {
        if (relativePaths.Count == 0)
        {
            return [localPath];
        }

        var resolved = new List<string>();
        foreach (var rel in relativePaths)
        {
            var full = Path.GetFullPath(Path.Combine(localPath, rel));
            if (Directory.Exists(full) || File.Exists(full))
            {
                resolved.Add(full);
            }
            else
            {
                Console.Error.WriteLine($"difftest: warning: search path missing: {full}");
            }
        }

        return resolved.Count > 0 ? resolved : [localPath];
    }

    private static async Task EnsureCloneAsync(
        string git,
        CorpusRepo repo,
        string localPath,
        CancellationToken cancellationToken
    )
    {
        if (Directory.Exists(localPath))
        {
            var inside = await ProcessRunner.RunAsync(
                git,
                ["rev-parse", "--is-inside-work-tree"],
                localPath,
                cancellationToken
            );
            if (inside.ExitCode == 0 && inside.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.Delete(localPath, recursive: true);
        }

        var args = new List<string> { "clone", "--depth", "1", "--branch", repo.Ref, repo.Remote, localPath };
        var clone = await ProcessRunner.RunAsync(git, args, Directory.GetCurrentDirectory(), cancellationToken);
        if (clone.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git clone failed for {repo.Id}:{Environment.NewLine}{clone.StdErr}{clone.StdOut}"
            );
        }
    }

    private static string? FindGit()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

internal sealed record PreparedRepo(
    string Id,
    string Path,
    string Ref,
    string Sha,
    string Language,
    IReadOnlyList<string> SearchPaths
);
