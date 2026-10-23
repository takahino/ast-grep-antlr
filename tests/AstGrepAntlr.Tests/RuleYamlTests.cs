using AstGrepAntlr.Languages;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Rules;
using Xunit;

namespace AstGrepAntlr.Tests;

public class RuleYamlTests
{
    [Fact]
    public void Loads_and_matches_rule()
    {
        var yaml = """
            id: no-println
            language: java
            severity: warning
            message: use a logger
            rule:
              pattern: System.out.println($A)
            """;
        var doc = RuleLoader.LoadRule(yaml);
        Assert.Equal("no-println", doc.Id);
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class A { void m(){ System.out.println(x); } }").Tree;
        var hits = new RuleEngine().Match(lang, tree, doc).ToList();
        Assert.NotEmpty(hits);
    }
}
