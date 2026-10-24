// Vendored from grammars-v4 sql/plsql/CSharp/PlSqlLexerBase.cs (Apache-2.0).
// Namespace adjusted to match Antlr4BuildTasks Package setting.
using System;
using System.IO;
using Antlr4.Runtime;

namespace AstGrepAntlr.Languages.PlSql;

public abstract class PlSqlLexerBase : Lexer
{
    private readonly ICharStream myinput;

    protected PlSqlLexerBase(ICharStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput)
    {
        myinput = input;
    }

    protected PlSqlLexerBase(ICharStream input)
        : base(input)
    {
        myinput = input;
    }

    public bool IsNewlineAtPos(int pos)
    {
        int la = myinput.LA(pos);
        return la == -1 || la == '\n';
    }

    public bool IsQQuoteDelimiter()
    {
        int openingOffset = TokenStartCharIndex + 2 - myinput.Index;
        int opening = myinput.LA(openingOffset);
        int closing = myinput.LA(-2);
        if (Char.IsHighSurrogate((char)opening))
        {
            int openingLow = myinput.LA(openingOffset + 1);
            if (Char.IsLowSurrogate((char)openingLow))
            {
                opening = Char.ConvertToUtf32((char)opening, (char)openingLow);
            }
        }

        if (Char.IsLowSurrogate((char)closing))
        {
            int closingHigh = myinput.LA(-3);
            if (Char.IsHighSurrogate((char)closingHigh))
            {
                closing = Char.ConvertToUtf32((char)closingHigh, (char)closing);
            }
        }

        return opening switch
        {
            '[' => closing == ']',
            '{' => closing == '}',
            '<' => closing == '>',
            '(' => closing == ')',
            _ => closing == opening,
        };
    }
}
