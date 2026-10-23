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

            var sha = repo.Sha;
            if (string.IsNullOrWhiteSpace(sha))
            {
                var rev = await ProcessRunner.RunAsync(git, ["rev-parse", "HEAD"], localPath, cancellationToken);
                if (rev.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"git rev-parse failed for {repo.Id}:{Environment.NewLine}{rev.StdErr}"
                    );
                }

                sha = rev.StdOut.Trim();
            }

            prepared.Add(new PreparedRepo(repo.Id, localPath, repo.Ref, sha));
        }

        return prepared;
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

internal sealed record PreparedRepo(string Id, string Path, string Ref, string Sha);
