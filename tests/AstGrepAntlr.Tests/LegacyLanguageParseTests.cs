using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using Xunit;

namespace AstGrepAntlr.Tests;

/// <summary>Built-in ANTLR languages for legacy / enterprise code (COBOL, VB6, SQL dialects).</summary>
public class LegacyLanguageParseTests
{
    private static readonly LanguageRegistry Registry = BuiltinLanguages.CreateRegistry();

    private const string CobolProgram =
        "IDENTIFICATION DIVISION.\nPROGRAM-ID. HELLO.\nPROCEDURE DIVISION.\n    MOVE WS-A TO WS-B.\n";

    private const string FixedCobol = """
        000100 IDENTIFICATION DIVISION.                                         HELLO001
        000200 PROGRAM-ID. HELLO.                                               HELLO002
        000300* COMMENT LINE: MOVE NOTHING TO NOWHERE.                          HELLO003
        000400 PROCEDURE DIVISION.                                              HELLO004
        000500     MOVE WS-A TO WS-B.                                           HELLO005
        000600     STOP RUN.                                                    HELLO006

        """;

    [Theory]
    [InlineData("cobol", CobolProgram)]
    [InlineData("cobol", FixedCobol)]
    [InlineData("vb6", "Public Sub Hello(ByVal name As String)\r\n    MsgBox \"Hello \" & name\r\nEnd Sub\r\n")]
    [InlineData("tsql", "SELECT TOP 10 id FROM dbo.users WHERE age > 20;\nGO\n")]
    [InlineData("postgresql", "SELECT id FROM users WHERE age > 20;\n")]
    [InlineData("mysql", "SELECT id FROM users WHERE age > 20 LIMIT 10;\n")]
    public void Parses_without_diagnostics(string id, string source)
    {
        var result = new TwoStageParser().Parse(Registry.Get(id), source);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("cobol", CobolProgram, "MOVE $A TO $B", "WS-A")]
    [InlineData("cobol", CobolProgram, "MOVE $A TO WS-B.", "WS-A")]
    [InlineData("vb6", "Sub M()\r\n    x  =  1\r\n    MsgBox \"hi\"\r\nEnd Sub\r\n", "MsgBox $A", "\"hi\"")]
    [InlineData("vb6", "Sub M()\r\n    x  =  1\r\nEnd Sub\r\n", "$A = 1", "x")]
    [InlineData("tsql", "UPDATE dbo.users SET name = N'x' WHERE id = 1;\n", "UPDATE $T SET $$$ WHERE $A", "id = 1")]
    [InlineData("postgresql", "SELECT id FROM users WHERE age > 20;\n", "SELECT $$$ FROM $A WHERE $C", "users")]
    [InlineData("mysql", "SELECT id FROM users WHERE age > 20;\n", "SELECT $$$ FROM $T WHERE $A", "age > 20")]
    public void Pattern_matches_and_captures(string id, string source, string pattern, string expectedA)
    {
        var lang = Registry.Get(id);
        var tree = new TwoStageParser().Parse(lang, source).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec(pattern));
        var hit = Assert.Single(new PatternMatcher().MatchAll(tree, compiled));
        Assert.Equal(expectedA, hit.Meta["A"].Text);
    }

    [Fact]
    public void Fixed_format_cobol_keeps_original_positions()
    {
        var lang = Registry.Get("cobol");
        var tree = new TwoStageParser().Parse(lang, FixedCobol).Tree;
        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("MOVE $A TO $B"));
        var hit = Assert.Single(new PatternMatcher().MatchAll(tree, compiled));

        // The commented-out MOVE in column 7 `*` line must not match; the real one starts at column 12.
        Assert.Equal(FixedCobol.IndexOf("MOVE WS-A", StringComparison.Ordinal), tree[hit.NodeIndex].Start);
        Assert.Equal("WS-B", hit.Meta["B"].Text);
    }

    [Fact]
    public void Fixed_format_cobol_counts_wide_characters_as_two_columns()
    {
        // Shift_JIS DBCS literals take two byte columns each, so the identification area starts
        // earlier in characters. If it were not blanked, "SEQ00001" would be a parse error.
        var display = "000500     DISPLAY '日本語テキスト'.";
        var width = display.Length + "日本語テキスト".Length; // wide chars count twice
        var source =
            "000100 IDENTIFICATION DIVISION.\n000200 PROGRAM-ID. HELLO.\n000300 PROCEDURE DIVISION.\n"
            + display
            + new string(' ', 72 - width)
            + "SEQ00001\n";
        var lang = Registry.Get("cobol");
        var result = new TwoStageParser().Parse(lang, source);
        Assert.Empty(result.Diagnostics);

        var compiled = new PatternCompiler().Compile(lang, new PatternSpec("DISPLAY $A"));
        var hit = Assert.Single(new PatternMatcher().MatchAll(result.Tree, compiled));
        Assert.Equal("'日本語テキスト'", hit.Meta["A"].Text);
    }

    [Theory]
    [InlineData("cobol85", "cobol")]
    [InlineData("mssql", "tsql")]
    [InlineData("sqlserver", "tsql")]
    [InlineData("postgres", "postgresql")]
    [InlineData("pgsql", "postgresql")]
    public void Alias_resolves_to_language(string alias, string id) => Assert.Equal(id, Registry.Get(alias).Id);
}
