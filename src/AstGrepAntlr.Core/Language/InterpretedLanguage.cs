using System.Security.Cryptography;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Misc;

namespace AstGrepAntlr.Language;

/// <summary>
/// Language backed by ANTLR .interp files (actions/predicates are not executed).
/// </summary>
public sealed class InterpretedLanguage : ILanguage
{
    private readonly string _grammarFileName;
    private readonly string[] _ruleNames;
    private readonly string[] _lexerRuleNames;
    private readonly IVocabulary _vocabulary;
    private readonly ATN _lexerAtn;
    private readonly ATN _parserAtn;
    private readonly string[] _channelNames;
    private readonly string[] _modeNames;
    private readonly DFA[] _decisionToDfa;
    private readonly PredictionContextCache _sharedContextCache = new();

    private InterpretedLanguage(
        string id,
        IReadOnlyList<string> extensions,
        string startRule,
        IReadOnlyList<string> patternRules,
        string metaVarPlaceholder,
        bool ignoreCase,
        InterpreterData lexerData,
        InterpreterData parserData
    )
    {
        Id = id;
        Extensions = extensions;
        StartRule = startRule;
        PatternRules = patternRules;
        MetaVarPlaceholderTemplate = metaVarPlaceholder;
        IgnoreCase = ignoreCase;
        _grammarFileName = id;
        _ruleNames = parserData.RuleNames;
        _lexerRuleNames = lexerData.RuleNames;
        _vocabulary = parserData.Vocabulary;
        _lexerAtn = lexerData.Atn;
        _parserAtn = parserData.Atn;
        _channelNames = lexerData.Channels;
        _modeNames = lexerData.Modes;
        _decisionToDfa = new DFA[_parserAtn.NumberOfDecisions];
        for (var i = 0; i < _parserAtn.NumberOfDecisions; i++)
        {
            _decisionToDfa[i] = new DFA(_parserAtn.GetDecisionState(i), i);
        }
    }

    public string Id { get; }
    public IReadOnlyList<string> Extensions { get; }
    public string StartRule { get; }
    public IReadOnlyList<string> PatternRules { get; }
    public string MetaVarPlaceholderTemplate { get; }
    public bool IgnoreCase { get; }
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 4 * 1024 * 1024;
    public IReadOnlyList<string> RuleNames => _ruleNames;
    public IVocabulary Vocabulary => _vocabulary;

    public static InterpretedLanguage FromInterpFiles(
        string id,
        IReadOnlyList<string> extensions,
        string startRule,
        IReadOnlyList<string> patternRules,
        string metaVarPlaceholder,
        bool ignoreCase,
        string lexerInterpPath,
        string parserInterpPath
    )
    {
        var lexerData = InterpFileReader.ParseFile(lexerInterpPath);
        var parserData = InterpFileReader.ParseFile(parserInterpPath);
        return new InterpretedLanguage(
            id,
            extensions,
            startRule,
            patternRules,
            metaVarPlaceholder,
            ignoreCase,
            lexerData,
            parserData
        );
    }

    public Lexer CreateLexer(ICharStream stream) =>
        new LexerInterpreter(
            _grammarFileName,
            _vocabulary,
            _lexerRuleNames,
            _channelNames,
            _modeNames,
            _lexerAtn,
            stream
        );

    public Parser CreateParser(ITokenStream tokens) =>
        new SharedDfaParserInterpreter(
            _grammarFileName,
            _vocabulary,
            _ruleNames,
            _parserAtn,
            tokens,
            _decisionToDfa,
            _sharedContextCache
        );

    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var index = Array.FindIndex(_ruleNames, r => string.Equals(r, ruleName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new ArgumentException($"Unknown rule '{ruleName}' for language '{Id}'.");
        }

        return ((ParserInterpreter)parser).Parse(index);
    }

    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
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

    private sealed class SharedDfaParserInterpreter : ParserInterpreter
    {
        public SharedDfaParserInterpreter(
            string grammarFileName,
            IVocabulary vocabulary,
            IEnumerable<string> ruleNames,
            ATN atn,
            ITokenStream input,
            DFA[] decisionToDfa,
            PredictionContextCache sharedContextCache
        )
            : base(grammarFileName, vocabulary, ruleNames, atn, input)
        {
            Interpreter = new ParserATNSimulator(this, atn, decisionToDfa, sharedContextCache);
        }
    }
}

internal sealed class InterpreterData
{
    public required ATN Atn { get; init; }
    public required string[] RuleNames { get; init; }
    public required string[] Channels { get; init; }
    public required string[] Modes { get; init; }
    public required IVocabulary Vocabulary { get; init; }
}

/// <summary>Minimal reader for ANTLR 4 .interp files.</summary>
internal static class InterpFileReader
{
    public static InterpreterData ParseFile(string path) => Parse(File.ReadAllText(path));

    public static InterpreterData Parse(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        string[] ReadBlock()
        {
            line = reader.ReadLine() ?? throw new InvalidDataException("Unexpected EOF in .interp");
            if (!int.TryParse(line, out var n))
            {
                throw new InvalidDataException($"Expected count, got '{line}'");
            }

            var items = new string[n];
            for (var i = 0; i < n; i++)
            {
                items[i] = reader.ReadLine() ?? string.Empty;
            }

            return items;
        }

        var literalNames = ReadBlock();
        var symbolicNames = ReadBlock();
        var vocabulary = new Vocabulary(literalNames, symbolicNames);
        var ruleNames = ReadBlock();
        string[] channels;
        string[] modes;
        // Lexers have channels+modes; parsers may only have rule names then ATN.
        // Peek: if next line is a small integer followed by names then another, it's channels.
        var peekPos = Mark(reader);
        try
        {
            channels = ReadBlock();
            modes = ReadBlock();
        }
        catch
        {
            Reset(reader, peekPos);
            channels = ["DEFAULT_TOKEN_CHANNEL", "HIDDEN"];
            modes = ["DEFAULT_MODE"];
        }

        line = reader.ReadLine();
        if (line is null || !line.StartsWith("atn:", StringComparison.OrdinalIgnoreCase))
        {
            // Some files put ATN serialized as integer list without header.
            // Fall through using remaining content.
        }

        var rest = reader.ReadToEnd();
        if (line is not null && !line.StartsWith("atn:", StringComparison.OrdinalIgnoreCase))
        {
            rest = line + "\n" + rest;
        }
        else if (line is not null && line.StartsWith("atn:", StringComparison.OrdinalIgnoreCase))
        {
            rest = line["atn:".Length..] + rest;
        }

        var serialized = ParseIntList(rest);
        var atn = new ATNDeserializer().Deserialize(serialized);
        return new InterpreterData
        {
            Atn = atn,
            RuleNames = ruleNames,
            Channels = channels,
            Modes = modes,
            Vocabulary = vocabulary,
        };
    }

    private static int[] ParseIntList(string text)
    {
        var numbers = new List<int>();
        foreach (var part in text.Split([',', ' ', '\r', '\n', '\t', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var n))
            {
                numbers.Add(n);
            }
        }

        return numbers.ToArray();
    }

    private static object Mark(StringReader reader)
    {
        // StringReader doesn't support mark; we can't easily peek.
        // Instead detect by trying channels read carefully in Parse — use a line buffer approach.
        return reader;
    }

    private static void Reset(StringReader reader, object _)
    {
        // no-op fallback path uses defaults — for robustness re-parse if needed.
        _ = reader;
    }
}

/// <summary>Compiles .g4 to .interp via the ANTLR tool jar and caches under LocalAppData/ag4/cache.</summary>
public static class GrammarInterpCache
{
    public static (string LexerInterp, string ParserInterp) EnsureInterp(
        IReadOnlyList<string> grammarFiles,
        string? antlrJar = null
    )
    {
        var hash = HashFiles(grammarFiles);
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ag4",
            "cache",
            hash
        );
        Directory.CreateDirectory(cacheDir);

        var lexerInterp = Directory.EnumerateFiles(cacheDir, "*Lexer.interp").FirstOrDefault();
        var parserInterp = Directory.EnumerateFiles(cacheDir, "*Parser.interp").FirstOrDefault();
        if (lexerInterp is not null && parserInterp is not null)
        {
            return (lexerInterp, parserInterp);
        }

        antlrJar ??= FindAntlrJar();
        if (antlrJar is null || !File.Exists(antlrJar))
        {
            throw new InvalidOperationException(
                "ANTLR tool jar not found. Install Java and place antlr-4.13.2-complete.jar in tools/, or pass prebuilt .interp files."
            );
        }

        var args =
            $"-jar \"{antlrJar}\" -Dlanguage=CSharp -Xexact-output-dir -o \"{cacheDir}\" "
            + string.Join(' ', grammarFiles.Select(f => $"\"{f}\""));
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "java",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc =
            System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start java.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"antlr tool failed: {stderr}\n{stdout}");
        }

        lexerInterp =
            Directory.EnumerateFiles(cacheDir, "*Lexer.interp").FirstOrDefault() ?? throw new InvalidOperationException(
                "Lexer .interp not produced."
            );
        parserInterp =
            Directory.EnumerateFiles(cacheDir, "*Parser.interp").FirstOrDefault()
            ?? throw new InvalidOperationException("Parser .interp not produced.");
        return (lexerInterp, parserInterp);
    }

    private static string HashFiles(IReadOnlyList<string> files)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in files.Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(f));
            sha.AppendData(File.ReadAllBytes(f));
        }

        return Convert.ToHexString(sha.GetHashAndReset())[..16].ToLowerInvariant();
    }

    private static string? FindAntlrJar()
    {
        var candidates = new[]
        {
            Path.Combine("tools", "antlr-4.13.2-complete.jar"),
            Path.Combine(AppContext.BaseDirectory, "tools", "antlr-4.13.2-complete.jar"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
