using Antlr4.Runtime;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Parsing;
using Xunit;

namespace AstGrepAntlr.Tests;

public class AntlrOffsetTests
{
    [Fact]
    public void CharStreams_fromString_uses_code_point_indexes()
    {
        var lang = new JavaLanguage();
        var src = "class A { String s = \"\U0001F600\"; }";
        var stream = CharStreams.fromString(src);
        var lexer = lang.CreateLexer(stream);
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();
        var stringLit = tokens.GetTokens().First(t => t.Text is not null && t.Text.Contains('\uD83D'));
        // Code-point length of "😀" is 3 (" + 1 codepoint + ").
        var spanLen = stringLit.StopIndex - stringLit.StartIndex + 1;
        Assert.Equal(3, spanLen);
        Assert.Equal(src.EnumerateRunes().Count() + 1, src.Length); // +1 UTF-16 unit from surrogate pair
    }

    [Fact]
    public void TreeBuilder_converts_to_utf16_so_slices_match_source()
    {
        var lang = new JavaLanguage();
        var src = "class A { String s = \"\U0001F600\"; }";
        var tree = new TwoStageParser().Parse(lang, src).Tree;
        var hit = false;
        for (var i = 0; i < tree.NodeCount; i++)
        {
            var t = tree.Text(i);
            if (!t.Contains("\U0001F600", StringComparison.Ordinal))
            {
                continue;
            }

            hit = true;
            Assert.Equal(src[tree[i].Start..tree[i].End], t);
            // UTF-16 length of "😀" is 4.
            if (t is "\"\U0001F600\"")
            {
                Assert.Equal(4, tree[i].End - tree[i].Start);
            }
        }

        Assert.True(hit);
    }
}
