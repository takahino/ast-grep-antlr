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

    [Theory]
    [MemberData(nameof(OracleCases))]
    public void Matcher_agrees_with_git_check_ignore(string id, string gitignore, string relativePath, bool asDirectory)
    {
        _ = id;
        if (!TempGitRepo.IsGitAvailable())
        {
            return;
        }

        using var repo = new TempGitRepo().Init();
        repo.WriteGitignore(gitignore);

        var full = Path.Combine(repo.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (asDirectory)
        {
            Directory.CreateDirectory(full);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }

        // Materialize paths for git (untracked is fine for check-ignore).
        var expected = repo.CheckIgnore(relativePath.Replace('\\', '/'));

        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, gitignore.Split('\n'));
        var actual = matcher.IsIgnored(relativePath.Replace('\\', '/'), isDirectory: asDirectory);

        Assert.Equal(expected, actual);
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

        var walker = new FileWalker();
        var found = walker
            .Enumerate([repo.Path], new FileWalkOptions { Extensions = [".log"], IncludeHidden = true })
            .Select(f => Path.GetRelativePath(repo.Path, f).Replace('\\', '/'))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        // Root *.log ignores all .log; pkg/!debug.log un-ignores only under pkg.
        // git: root.log ignored; pkg/other.log ignored by root; pkg/debug.log may be un-ignored.
        Assert.DoesNotContain("root.log", found);
        Assert.DoesNotContain("pkg/other.log", found);
        // Negation in nested gitignore: align with git check-ignore when possible.
        var debugIgnored = repo.CheckIgnore("pkg/debug.log");
        if (!debugIgnored)
        {
            Assert.Contains("pkg/debug.log", found);
        }
    }
}
