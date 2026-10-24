using AstGrepAntlr.Search;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

public class FileWalkerTests
{
    [Fact]
    public void Hides_dotfiles_by_default()
    {
        using var dir = new TempDir();
        dir.WriteText("Keep.java", "class Keep {}");
        dir.WriteText(".hidden.java", "class Hidden {}");

        var found = new FileWalker()
            .Enumerate([dir.Path], new FileWalkOptions { Extensions = [".java"] })
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains("Keep.java", found);
        Assert.DoesNotContain(".hidden.java", found);
    }

    [Fact]
    public void IncludeHidden_lists_dotfiles()
    {
        using var dir = new TempDir();
        dir.WriteText(".hidden.java", "class Hidden {}");

        var found = new FileWalker()
            .Enumerate([dir.Path], new FileWalkOptions { Extensions = [".java"], IncludeHidden = true })
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains(".hidden.java", found);
    }

    [Fact]
    public void NoIgnore_walks_default_skip_dirs()
    {
        using var dir = new TempDir();
        dir.WriteText("bin/InBin.java", "class InBin {}");
        dir.WriteText("Ok.java", "class Ok {}");

        var skipped = new FileWalker()
            .Enumerate([dir.Path], new FileWalkOptions { Extensions = [".java"] })
            .Select(f => Path.GetRelativePath(dir.Path, f).Replace('\\', '/'))
            .ToList();
        Assert.DoesNotContain("bin/InBin.java", skipped);
        Assert.Contains("Ok.java", skipped);

        var withNoIgnore = new FileWalker()
            .Enumerate([dir.Path], new FileWalkOptions { Extensions = [".java"], NoIgnore = true })
            .Select(f => Path.GetRelativePath(dir.Path, f).Replace('\\', '/'))
            .ToList();
        Assert.Contains("bin/InBin.java", withNoIgnore);
    }

    [Fact]
    public void Files_and_ignores_globs_filter()
    {
        using var dir = new TempDir();
        dir.WriteText("Keep.java", "class Keep {}");
        dir.WriteText("Skip.java", "class Skip {}");
        dir.WriteText("Other.txt", "x");

        var found = new FileWalker()
            .Enumerate(
                [dir.Path],
                new FileWalkOptions
                {
                    Files = ["**/*.java"],
                    Ignores = ["Skip.java"],
                    BaseDirectory = dir.Path,
                }
            )
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains("Keep.java", found);
        Assert.DoesNotContain("Skip.java", found);
        Assert.DoesNotContain("Other.txt", found);
    }

    [Fact]
    public void Respects_gitignore_unless_NoIgnore()
    {
        if (!TempGitRepo.IsGitAvailable())
        {
            return;
        }

        using var repo = new TempGitRepo().Init();
        repo.WriteGitignore("Ignored.java\n");
        File.WriteAllText(Path.Combine(repo.Path, "Ignored.java"), "class Ignored {}");
        File.WriteAllText(Path.Combine(repo.Path, "Keep.java"), "class Keep {}");

        var ignored = new FileWalker()
            .Enumerate([repo.Path], new FileWalkOptions { Extensions = [".java"] })
            .Select(Path.GetFileName)
            .ToList();
        Assert.DoesNotContain("Ignored.java", ignored);
        Assert.Contains("Keep.java", ignored);

        var forced = new FileWalker()
            .Enumerate([repo.Path], new FileWalkOptions { Extensions = [".java"], NoIgnore = true })
            .Select(Path.GetFileName)
            .ToList();
        Assert.Contains("Ignored.java", forced);
    }
}
