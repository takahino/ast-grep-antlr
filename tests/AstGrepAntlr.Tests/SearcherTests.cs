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
}
