using AstGrepAntlr.Search;
using Xunit;

namespace AstGrepAntlr.Tests;

public class PathGlobTests
{
    [Theory]
    [InlineData("src/Foo.java", "*.java", true)]
    [InlineData("src/Foo.java", "src/*.java", true)]
    [InlineData("src/Foo.java", "src/**/*.java", true)]
    [InlineData("src/Foo.java", "**/Foo.java", true)]
    [InlineData("src/Foo.java", "Bar.java", false)]
    [InlineData("a/b/c.txt", "**/c.txt", true)]
    [InlineData("a/b/c.txt", "a/*/c.txt", true)]
    [InlineData("a/b/c.txt", "a/*/d.txt", false)]
    [InlineData("Foo.java", "/Foo.java", true)]
    [InlineData("dir/Foo.java", "/Foo.java", false)]
    [InlineData("x.cs", "*.[cj]s", true)]
    [InlineData("x.ts", "*.[cj]s", false)]
    public void IsMatch_basic(string path, string pattern, bool expected) =>
        Assert.Equal(expected, PathGlob.IsMatch(path, pattern));

    [Fact]
    public void MatchesGlobs_exclude_with_bang()
    {
        Assert.True(PathGlob.MatchesGlobs("Keep.java", ["*.java", "!Skip.java"]));
        Assert.False(PathGlob.MatchesGlobs("Skip.java", ["*.java", "!Skip.java"]));
    }

    [Fact]
    public void Basename_fallback_matches_any_segment()
    {
        Assert.True(PathGlob.IsMatch("a/node_modules/x", "node_modules", basenameOnlyFallback: true));
        Assert.False(PathGlob.IsMatch("a/node_modules/x", "node_modules", basenameOnlyFallback: false));
    }
}
