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
public sealed class InterpretedLanguage : IAntlrLanguage
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
        IReadOnlyList<string>? metaVarVariants,
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
        MetaVarVariants = metaVarVariants is { Count: > 0 } ? metaVarVariants : ["{0}"];
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
    public IReadOnlyList<string> MetaVarVariants { get; }
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
        string parserInterpPath,
        IReadOnlyList<string>? metaVarVariants = null
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
            metaVarVariants,
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

    // An .interp file is a sequence of `<header>:` sections, each followed by one entry per line
    // and terminated by a blank line, ending with the serialized ATN.
    public static InterpreterData Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var cursor = 0;

        var literalNames = ReadSection(lines, ref cursor, "token literal names:");
        var symbolicNames = ReadSection(lines, ref cursor, "token symbolic names:");
        var ruleNames = ReadSection(lines, ref cursor, "rule names:");

        // Only lexer files carry channels and modes; parser files go straight to the ATN.
        var channels = TryReadSection(lines, ref cursor, "channel names:") ?? ["DEFAULT_TOKEN_CHANNEL", "HIDDEN"];
        var modes = TryReadSection(lines, ref cursor, "mode names:") ?? ["DEFAULT_MODE"];

        var atnStart = Array.FindIndex(lines, cursor, l => l.StartsWith("atn:", StringComparison.Ordinal));
        if (atnStart < 0)
        {
            throw new InvalidDataException("Missing 'atn:' section in .interp file.");
        }

        var atn = new ATNDeserializer().Deserialize(ParseIntList(lines, atnStart));
        return new InterpreterData
        {
            Atn = atn,
            RuleNames = ruleNames,
            Channels = channels,
            Modes = modes,
            Vocabulary = new Vocabulary(ToNames(literalNames), ToNames(symbolicNames)),
        };
    }

    private static string[] ReadSection(string[] lines, ref int cursor, string header) =>
        TryReadSection(lines, ref cursor, header)
        ?? throw new InvalidDataException($"Missing '{header}' section in .interp file.");

    /// <summary>
    /// Consumes the entries of <paramref name="header"/> if that is the next section, leaving the
    /// cursor untouched otherwise so an optional section can be probed without losing position.
    /// </summary>
    private static string[]? TryReadSection(string[] lines, ref int cursor, string header)
    {
        var i = cursor;
        while (i < lines.Length && lines[i].Length == 0)
        {
            i++;
        }

        if (i >= lines.Length || !string.Equals(lines[i], header, StringComparison.Ordinal))
        {
            return null;
        }

        i++;
        var items = new List<string>();
        while (i < lines.Length && lines[i].Length > 0)
        {
            items.Add(lines[i]);
            i++;
        }

        cursor = i;
        return items.ToArray();
    }

    /// <summary>ANTLR writes absent names as the literal text "null"; Vocabulary expects a real null.</summary>
    private static string[] ToNames(string[] raw)
    {
        var names = new string[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            names[i] = string.Equals(raw[i], "null", StringComparison.Ordinal) ? null! : raw[i];
        }

        return names;
    }

    private static int[] ParseIntList(string[] lines, int start)
    {
        var numbers = new List<int>();
        for (var i = start; i < lines.Length; i++)
        {
            foreach (var part in lines[i].Split([',', ' ', '\t', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var n))
                {
                    numbers.Add(n);
                }
            }
        }

        return numbers.ToArray();
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

        var searched = new List<string>();
        if (string.IsNullOrWhiteSpace(antlrJar))
        {
            antlrJar = AntlrJarLocator.Find(out searched);
        }
        else
        {
            searched.Add(antlrJar);
        }

        if (antlrJar is null || !File.Exists(antlrJar))
        {
            var listed =
                searched.Count == 0
                    ? "(no candidates)"
                    : string.Join(Environment.NewLine, searched.Select(p => "  - " + p));
            throw new InvalidOperationException(
                "ANTLR tool jar not found. Install Java and place antlr-4.13.2-complete.jar under tools/, "
                    + "set ANTLR_JAR, or pass --antlr-jar."
                    + Environment.NewLine
                    + "Searched:"
                    + Environment.NewLine
                    + listed
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
}

/// <summary>
/// Resolves the ANTLR complete jar. Lookup order:
/// <c>--antlr-jar</c> / <see cref="ExplicitPath"/>, <c>ANTLR_JAR</c>,
/// sgconfig dir + ancestors <c>tools/</c>, cwd + ancestors <c>tools/</c>, <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class AntlrJarLocator
{
    public const string JarFileName = "antlr-4.13.2-complete.jar";

    /// <summary>Set from CLI <c>--antlr-jar</c>.</summary>
    public static string? ExplicitPath { get; set; }

    /// <summary>sgconfig.yml directory used as a search root.</summary>
    public static string? ConfigDirectory { get; set; }

    public static string? Find(out List<string> searched)
    {
        searched = [];
        foreach (var candidate in Candidates())
        {
            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        if (!string.IsNullOrWhiteSpace(ExplicitPath))
        {
            yield return Path.GetFullPath(ExplicitPath);
        }

        var env = Environment.GetEnvironmentVariable("ANTLR_JAR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return Path.GetFullPath(env);
        }

        if (!string.IsNullOrWhiteSpace(ConfigDirectory))
        {
            foreach (var p in AncestorToolsJars(ConfigDirectory))
            {
                yield return p;
            }
        }

        foreach (var p in AncestorToolsJars(Directory.GetCurrentDirectory()))
        {
            yield return p;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "tools", JarFileName);
        yield return Path.Combine(AppContext.BaseDirectory, JarFileName);
    }

    private static IEnumerable<string> AncestorToolsJars(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "tools", JarFileName);
            dir = dir.Parent;
        }
    }
}
