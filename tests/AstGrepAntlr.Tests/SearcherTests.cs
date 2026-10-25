using AstGrepAntlr.Languages;
using AstGrepAntlr.Search;
using Xunit;

namespace AstGrepAntlr.Tests;

public class SearcherTests
{
    [Fact]
    public void RunPattern_finds_println()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ag4-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "Hello.java");
            File.WriteAllText(file, "class Hello { void m() { System.out.println(1); } }\n");
            var registry = BuiltinLanguages.CreateRegistry();
            var hits = new Searcher()
                .RunPattern(
                    "System.out.println($A)",
                    new SearchOptions
                    {
                        Registry = registry,
                        LanguageId = "java",
                        Paths = [file],
                    }
                )
                .ToList();
            Assert.NotEmpty(hits);
            Assert.Equal("1", hits[0].MetaVariables["A"].Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RunPatternOnSources_finds_match_without_disk()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        var hits = new Searcher()
            .RunPatternOnSources(
                "foo($$$ARGS)",
                [new SourceInput("snippet.java", "class Demo { void m() { foo(1, 2); } }\n")],
                new SearchOptions { Registry = registry, LanguageId = "java" }
            )
            .ToList();
        Assert.Single(hits);
        Assert.Equal("snippet.java", hits[0].File);
        Assert.Equal("1, 2", hits[0].MetaVariables["ARGS"].Text);
    }

    [Fact]
    public void MaxResults_matches_prefix_of_unlimited()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        var sources = new List<SourceInput>();
        for (var i = 0; i < 20; i++)
        {
            var name = $"f{i:D2}.java";
            sources.Add(new SourceInput(name, $"class C{i} {{ void m() {{ foo({i}); }} }}\n"));
        }

        var all = new Searcher()
            .RunPatternOnSources(
                "foo($A)",
                sources,
                new SearchOptions
                {
                    Registry = registry,
                    LanguageId = "java",
                    MaxResults = int.MaxValue,
                }
            )
            .ToList();
        Assert.True(all.Count >= 5);

        var limited = new Searcher()
            .RunPatternOnSources(
                "foo($A)",
                sources,
                new SearchOptions
                {
                    Registry = registry,
                    LanguageId = "java",
                    MaxResults = 5,
                }
            )
            .ToList();

        Assert.Equal(5, limited.Count);
        Assert.Equal(
            all.Take(5).Select(m => (m.File, m.Start, m.Text)),
            limited.Select(m => (m.File, m.Start, m.Text))
        );
    }
}
