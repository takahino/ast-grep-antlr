// Vendored from grammars-v4 sql/plsql/CSharp/PlSqlParserBase.cs (Apache-2.0).
// Namespace adjusted to match Antlr4BuildTasks Package setting.
using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace AstGrepAntlr.Languages.PlSql;

public abstract class PlSqlParserBase : Parser
{
    private bool _isVersion10 = false;
    private bool _isVersion11 = true;
    private bool _isVersion12 = true;

    private bool _lastUnitWasPlsql = false;

    protected PlSqlParserBase(ITokenStream input)
        : base(input) { }

    protected PlSqlParserBase(ITokenStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput) { }

    public override void Reset()
    {
        _lastUnitWasPlsql = false;
        base.Reset();
    }

    public void setLastUnitPlsql() => _lastUnitWasPlsql = true;

    public void setLastUnitSql() => _lastUnitWasPlsql = false;

    public bool isLastUnitSql() => !_lastUnitWasPlsql;

    public bool isLastUnitPlsql() => _lastUnitWasPlsql;

    public bool isSolidusSeparator()
    {
        var stream = (CommonTokenStream)InputStream;
        var solidus = stream.LT(1);
        if (solidus == null || solidus.Type != PlSqlParser.SOLIDUS)
        {
            return false;
        }

        int solidusLine = solidus.Line;

        var prev = stream.LT(-1);
        if (prev != null && prev.Type != TokenConstants.EOF && prev.Line == solidusLine)
        {
            return false;
        }

        var next = stream.LT(2);
        if (next != null && next.Type != TokenConstants.EOF && next.Line == solidusLine)
        {
            return false;
        }

        return true;
    }

    public bool isVersion10() => _isVersion10;

    public bool isVersion11() => _isVersion11;

    public bool isVersion12() => _isVersion12;

    public bool setVersion10(bool value) => _isVersion10 = value;

    public bool setVersion11(bool value) => _isVersion11 = value;

    public bool setVersion12(bool value) => _isVersion12 = value;

    public bool IsNotNumericFunction()
    {
        var lt1 = ((CommonTokenStream)InputStream).LT(1);
        var lt2 = ((CommonTokenStream)InputStream).LT(2);
        if (
            (
                lt1.Type == PlSqlParser.SUM
                || lt1.Type == PlSqlParser.COUNT
                || lt1.Type == PlSqlParser.AVG
                || lt1.Type == PlSqlParser.MIN
                || lt1.Type == PlSqlParser.MAX
                || lt1.Type == PlSqlParser.ROUND
                || lt1.Type == PlSqlParser.LEAST
                || lt1.Type == PlSqlParser.GREATEST
            )
            && lt2.Type == PlSqlParser.LEFT_PAREN
        )
        {
            return false;
        }

        return true;
    }

    public bool isNotStartOfJoin()
    {
        var lt1 = ((CommonTokenStream)InputStream).LT(1);
        if (
            lt1.Type == PlSqlParser.INNER
            || lt1.Type == PlSqlParser.CROSS
            || lt1.Type == PlSqlParser.NATURAL
            || lt1.Type == PlSqlParser.PARTITION
            || lt1.Type == PlSqlParser.FULL
            || lt1.Type == PlSqlParser.LEFT
            || lt1.Type == PlSqlParser.RIGHT
            || lt1.Type == PlSqlParser.OUTER
        )
        {
            return false;
        }

        return true;
    }
}
