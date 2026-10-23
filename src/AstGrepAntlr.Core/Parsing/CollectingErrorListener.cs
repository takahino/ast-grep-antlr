using Antlr4.Runtime;

namespace AstGrepAntlr.Parsing;

public sealed record ParseDiagnostic(int Line, int Column, string Message, bool IsLexer);

public sealed class CollectingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
{
    private readonly List<ParseDiagnostic> _diagnostics = [];

    public IReadOnlyList<ParseDiagnostic> Diagnostics => _diagnostics;

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e
    )
    {
        _diagnostics.Add(new ParseDiagnostic(line, charPositionInLine, msg, IsLexer: true));
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e
    )
    {
        _diagnostics.Add(new ParseDiagnostic(line, charPositionInLine, msg, IsLexer: false));
    }
}
