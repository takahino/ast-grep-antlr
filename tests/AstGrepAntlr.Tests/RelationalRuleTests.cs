using AstGrepAntlr.Languages;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Rules;
using Xunit;

namespace AstGrepAntlr.Tests;

public class RelationalRuleTests
{
    [Fact]
    public void Inside_default_stopBy_is_neighbor_only()
    {
        var neighborYaml = """
            id: inside-neighbor
            language: java
            rule:
              pattern: System.out.println($A)
              inside:
                kind: classDeclaration
            """;
        var endYaml = """
            id: inside-end
            language: java
            rule:
              pattern: System.out.println($A)
              inside:
                kind: classDeclaration
                stopBy: end
            """;
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class Outer { void m() { System.out.println(1); } }").Tree;
        Assert.Empty(new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(neighborYaml)).ToList());
        Assert.NotEmpty(new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(endYaml)).ToList());
    }

    [Fact]
    public void Precedes_neighbor_matches_next_named_sibling()
    {
        var yaml = """
            id: precedes-demo
            language: java
            rule:
              pattern: foo($A)
              precedes:
                pattern: bar($B)
            """;
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class T { void m() { x(foo(1), bar(2)); } }").Tree;
        var hits = new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(yaml)).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("1", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Follows_neighbor_matches_previous_named_sibling()
    {
        var yaml = """
            id: follows-demo
            language: java
            rule:
              pattern: bar($A)
              follows:
                pattern: foo($B)
            """;
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class T { void m() { x(foo(1), bar(2)); } }").Tree;
        var hits = new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(yaml)).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal("2", hits[0].Meta["A"].Text);
    }

    [Fact]
    public void Precedes_stopBy_end_skips_intermediate_siblings()
    {
        var endYaml = """
            id: precedes-end
            language: java
            rule:
              pattern: foo($A)
              precedes:
                pattern: baz($B)
                stopBy: end
            """;
        var neighborYaml = """
            id: precedes-neighbor
            language: java
            rule:
              pattern: foo($A)
              precedes:
                pattern: baz($B)
                stopBy: neighbor
            """;
        var lang = new JavaLanguage();
        var tree = new TwoStageParser().Parse(lang, "class T { void m() { x(foo(1), bar(2), baz(3)); } }").Tree;
        Assert.NotEmpty(new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(endYaml)).ToList());
        Assert.Empty(new RuleEngine().Match(lang, tree, RuleLoader.LoadRule(neighborYaml)).ToList());
    }
}
