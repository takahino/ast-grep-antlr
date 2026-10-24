using AstGrepAntlr.Search;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

/// <summary>
/// Compares <see cref="GitIgnoreMatcher"/> to <c>git check-ignore</c> across pattern variations.
/// </summary>
[Trait("Category", "Variation")]
public class GitIgnoreOracleTests
{
    public static IEnumerable<object[]> OracleCases()
    {
        // id, gitignore body, relative path, create as directory?
        yield return ["star-java", "*.java\n", "Foo.java", false];
        yield return ["star-java-kept", "*.java\n", "Foo.txt", false];
        yield return ["dir-build", "build/\n", "build/out.txt", false];
        yield return ["dir-build-dir", "build/\n", "build", true];
        yield return ["anchored-root", "/secret.txt\n", "secret.txt", false];
        yield return ["anchored-nested", "/secret.txt\n", "sub/secret.txt", false];
        yield return ["double-star", "**/tmp.log\n", "a/b/tmp.log", false];
        yield return ["negate", "*.log\n!keep.log\n", "keep.log", false];
        yield return ["negate-ignored", "*.log\n!keep.log\n", "drop.log", false];
        yield return ["char-class", "*.[cj]s\n", "x.cs", false];
        yield return ["char-class-miss", "*.[cj]s\n", "x.ts", false];
        yield return ["question", "?.txt\n", "a.txt", false];
        yield return ["question-miss", "?.txt\n", "ab.txt", false];
        yield return ["nested-path", "sub/*.o\n", "sub/a.o", false];
        yield return ["nested-path-miss", "sub/*.o\n", "other/a.o", false];
    }

    [Fact]
    public void Matcher_agrees_with_git_check_ignore_matrix()
    {
        if (!TempGitRepo.IsGitAvailable())
        {
            return;
        }

        var failures = new List<string>();
        foreach (var row in OracleCases())
        {
            var id = (string)row[0];
            var gitignore = (string)row[1];
            var relativePath = (string)row[2];
            var asDirectory = (bool)row[3];

            using var repo = new TempGitRepo().Init();
            repo.WriteGitignore(gitignore);

            var full = Path.Combine(repo.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (asDirectory)
            {
                Directory.CreateDirectory(full);
            }
            else
            {
                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.WriteAllText(full, "x");
            }

            var expected = repo.CheckIgnore(relativePath.Replace('\\', '/'));
            var matcher = new GitIgnoreMatcher();
            matcher.AddFile(string.Empty, gitignore.Split('\n'));
            var actual = matcher.IsIgnored(relativePath.Replace('\\', '/'), isDirectory: asDirectory);
            if (expected != actual)
            {
                failures.Add($"{id}: git={expected} matcher={actual} path={relativePath}");
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void Nested_gitignore_scopes_to_subdirectory()
    {
        if (!TempGitRepo.IsGitAvailable())
        {
            return;
        }

        using var repo = new TempGitRepo().Init();
        repo.WriteGitignore("*.log\n");
        Directory.CreateDirectory(Path.Combine(repo.Path, "pkg"));
        repo.WriteGitignore("!debug.log\n", "pkg");
        File.WriteAllText(Path.Combine(repo.Path, "root.log"), "x");
        File.WriteAllText(Path.Combine(repo.Path, "pkg", "debug.log"), "x");
        File.WriteAllText(Path.Combine(repo.Path, "pkg", "other.log"), "x");

        var found = new FileWalker()
            .Enumerate([repo.Path], new FileWalkOptions { Extensions = [".log"], IncludeHidden = true })
            .Select(f => Path.GetRelativePath(repo.Path, f).Replace('\\', '/'))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.DoesNotContain("root.log", found);
        Assert.DoesNotContain("pkg/other.log", found);
        var debugIgnored = repo.CheckIgnore("pkg/debug.log");
        if (!debugIgnored)
        {
            Assert.Contains("pkg/debug.log", found);
        }
    }
}
