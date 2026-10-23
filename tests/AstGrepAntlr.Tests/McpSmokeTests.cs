using AstGrepAntlr.Languages;
using AstGrepAntlr.Mcp;
using Xunit;

namespace AstGrepAntlr.Tests;

public class McpSmokeTests
{
    [Fact]
    public void List_languages_returns_java()
    {
        McpContext.Registry = BuiltinLanguages.CreateRegistry();
        McpContext.Roots = [Directory.GetCurrentDirectory()];
        var json = Ag4Tools.ListLanguages();
        Assert.Contains("java", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sqlite", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_code_finds_println()
    {
        using var dir = new Harness.TempDir();
        var file = dir.WriteText("H.java", "class H { void m(){ System.out.println(7); } }\n");
        McpContext.Registry = BuiltinLanguages.CreateRegistry();
        McpContext.Roots = [dir.Path];
        var json = Ag4Tools.FindCode("System.out.println($A)", "java", [file]);
        Assert.Contains("7", json, StringComparison.Ordinal);
    }
}
