using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Cobol;

namespace AstGrepAntlr.Languages;

public sealed class CobolLanguage : IAntlrLanguage
{
    public string Id => "cobol";
    public IReadOnlyList<string> Extensions { get; } = [".cbl", ".cob", ".cpy"];
    public string StartRule => "startRule";
    public IReadOnlyList<string> PatternRules { get; } =
    [
        "statement",
        "sentence",
        "paragraph",
        "dataDescriptionEntry",
        "condition",
        "arithmeticExpression",
        "identifier",
        "literal",
        "startRule",
    ];

    // Cobol85 IDENTIFIER must start with a letter or digit (no leading underscore).
    public string MetaVarPlaceholderTemplate => "Ag4Mv{0}";
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0}.", ""];
    public bool IgnoreCase => true;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(60);
    public long MaxFileBytes => 8 * 1024 * 1024;

    public IReadOnlyList<string> RuleNames => Cobol85Parser.ruleNames;
    public IVocabulary Vocabulary => Cobol85Lexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream)
    {
        // Cobol85.g4 expects free-format input (grammars-v4 runs a separate preprocessor first).
        var text = stream.Size == 0 ? string.Empty : stream.GetText(Interval.Of(0, stream.Size - 1));
        if (!CobolFixedFormat.IsFixedFormat(text))
        {
            return new TrimmingLexer(stream);
        }

        return new TrimmingLexer(CharStreams.fromString(CobolFixedFormat.Normalize(text)));
    }

    /// <summary>
    /// DOT_FS is <c>'.'</c> plus the following whitespace. Shrink it to the period so a pattern
    /// ending in <c>.</c> (at EOF) matches source periods followed by a newline.
    /// </summary>
    private sealed class TrimmingLexer(ICharStream input) : Cobol85Lexer(input)
    {
        public override IToken NextToken()
        {
            var token = base.NextToken();
            if (token.Type == DOT_FS && token is CommonToken common)
            {
                common.StopIndex = common.StartIndex;
                common.Text = ".";
            }

            return token;
        }
    }

    public Parser CreateParser(ITokenStream tokens) => new Cobol85Parser(tokens);

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (Cobol85Parser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "startrule" => p.startRule(),
            "statement" => p.statement(),
            "sentence" => p.sentence(),
            "paragraph" => p.paragraph(),
            "datadescriptionentry" => p.dataDescriptionEntry(),
            "condition" => p.condition(),
            "arithmeticexpression" => p.arithmeticExpression(),
            "identifier" => p.identifier(),
            "literal" => p.literal(),
            _ => throw new ArgumentException($"Unknown COBOL rule '{ruleName}'."),
        };
    }

    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
        if (token.Type < 0)
        {
            return false;
        }

        var symbolic = vocabulary.GetSymbolicName(token.Type);
        var literal = vocabulary.GetLiteralName(token.Type);
        if (!string.IsNullOrEmpty(literal) && literal.StartsWith('\''))
        {
            var inner = literal.Trim('\'');
            if (inner.Length <= 2 && !inner.All(char.IsLetter))
            {
                return false;
            }

            return inner.Length > 1 && char.IsLetter(inner[0]);
        }

        return !string.IsNullOrEmpty(symbolic);
    }
}

/// <summary>
/// Reference-format (fixed-column) COBOL → free format, keeping every code point in place so
/// ANTLR offsets still map 1:1 onto the original source.
/// </summary>
internal static class CobolFixedFormat
{
    private const int SequenceEnd = 6; // columns 1-6
    private const int IndicatorColumn = 7;
    private const int ProgramTextEnd = 72; // columns 73+ are the identification area

    /// <summary>
    /// Document-level guess: most non-blank lines have a numeric/blank sequence area and a valid
    /// indicator. Pattern fragments (<c>MOVE $A TO $B</c>) fail this and are left untouched.
    /// </summary>
    public static bool IsFixedFormat(string text)
    {
        var lines = 0;
        var fixedLines = 0;
        foreach (var line in text.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Trim().Length == 0)
            {
                continue;
            }

            lines++;
            if (l.Length >= IndicatorColumn && IsSequenceArea(l) && IsIndicator(l[IndicatorColumn - 1]))
            {
                fixedLines++;
            }
        }

        return lines > 0 && fixedLines * 2 > lines;
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var nl = text.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? text.Length : nl;
            NormalizeLine(text, lineStart, lineEnd, sb);
            if (nl < 0)
            {
                break;
            }

            sb.Append('\n');
            lineStart = nl + 1;
        }

        return sb.ToString();
    }

    private static void NormalizeLine(string text, int start, int end, StringBuilder sb)
    {
        var hasCr = end > start && text[end - 1] == '\r';
        var contentEnd = hasCr ? end - 1 : end;
        var indicator = contentEnd - start >= IndicatorColumn ? text[start + IndicatorColumn - 1] : ' ';
        var commentLine = indicator is '*' or '/';

        var column = 0;
        for (var i = start; i < contentEnd; i++)
        {
            var isPair = char.IsHighSurrogate(text[i]) && i + 1 < contentEnd && char.IsLowSurrogate(text[i + 1]);
            var codePoint = isPair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            var firstColumn = column + 1;
            // Approximate the byte columns of DBCS encodings (Shift_JIS etc.): wide characters take two.
            column += IsWide(codePoint) ? 2 : 1;

            var keep =
                !commentLine
                && firstColumn > SequenceEnd
                && firstColumn != IndicatorColumn
                && firstColumn <= ProgramTextEnd;
            if (keep)
            {
                sb.Append(text, i, isPair ? 2 : 1);
            }
            else
            {
                // One space per code point: ANTLR offsets are code-point based.
                sb.Append(text[i] == '\t' ? '\t' : ' ');
            }

            if (isPair)
            {
                i++;
            }
        }

        if (hasCr)
        {
            sb.Append('\r');
        }
    }

    private static bool IsSequenceArea(string line)
    {
        for (var i = 0; i < SequenceEnd; i++)
        {
            if (!char.IsAsciiDigit(line[i]) && line[i] != ' ')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIndicator(char c) => c is ' ' or '*' or '/' or '-' or 'D' or 'd' or '$';

    private static bool IsWide(int codePoint) => codePoint > 0xFF && codePoint is not (>= 0xFF61 and <= 0xFF9F); // half-width katakana is single-byte
}
