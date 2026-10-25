using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using AstGrepAntlr.Language;
using AstGrepAntlr.Text;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Bench;

/// <summary>
/// Explains where SLL bails and which decisions make the LL retry expensive. Drives the
/// <see cref="IAntlrLanguage"/> factories directly, so it works for every builtin ANTLR grammar
/// and for <see cref="InterpretedLanguage"/> without touching the production parse path.
/// </summary>
internal static class AntlrDiagnostics
{
    public static void Run(IAntlrLanguage language, IReadOnlyList<string> files, int top, TextWriter output)
    {
        var sllFailures = new Dictionary<string, int>(StringComparer.Ordinal);
        var sllOk = 0;
        var sllOnly = 0;
        var realErrors = 0;
        var llTicks = 0L;
        DecisionTotals[]? totals = null;
        ATN? atn = null;

        foreach (var file in files)
        {
            if (!SourceFile.TryRead(file, out var source, out _))
            {
                continue;
            }

            var tokens = Lex(language, source!.Text);
            var failure = TrySll(language, tokens);
            if (failure is null)
            {
                sllOk++;
                continue;
            }

            sllFailures[failure] = sllFailures.GetValueOrDefault(failure) + 1;

            tokens.Seek(0);
            var parser = language.CreateParser(tokens);
            parser.RemoveErrorListeners();
            var errors = new CountingErrorListener();
            parser.AddErrorListener(errors);
            // Profile swaps in a ProfilingATNSimulator, so the prediction mode has to be set after it.
            parser.Profile = true;
            parser.Interpreter.PredictionMode = PredictionMode.LL;
            var sw = Stopwatch.StartNew();
            language.InvokeRule(parser, language.StartRule);
            llTicks += sw.ElapsedTicks;

            if (errors.Count == 0)
            {
                sllOnly++;
            }
            else
            {
                realErrors++;
            }

            atn ??= parser.Interpreter.atn;
            var infos = new ParseInfo((ProfilingATNSimulator)parser.Interpreter).getDecisionInfo();
            totals ??= new DecisionTotals[infos.Length];
            for (var d = 0; d < infos.Length; d++)
            {
                totals[d].Add(infos[d]);
            }
        }

        var failed = sllOnly + realErrors;
        output.WriteLine(
            $"diagnose lang={language.Id} files={sllOk + failed} sll_ok={sllOk} sll_failed={failed} "
                + $"(ll_clean={sllOnly} ll_errors={realErrors}) ll_profiled_ms={llTicks * 1000.0 / Stopwatch.Frequency:F1}"
        );

        output.WriteLine();
        output.WriteLine($"SLL bail sites (top {top}): count  rule-stack  @state  token");
        foreach (var (site, count) in sllFailures.OrderByDescending(kv => kv.Value).Take(top))
        {
            output.WriteLine($"  {count, 5}  {site}");
        }

        if (totals is null || atn is null)
        {
            return;
        }

        WriteDecisionTable(output, "LL decisions by prediction time", totals, atn, language, top, t => t.Time100Ns);
        WriteDecisionTable(output, "LL decisions by LL_Fallback count", totals, atn, language, top, t => t.LlFallback);
        WriteDecisionTable(output, "LL decisions by LL_MaxLook", totals, atn, language, top, t => t.LlMaxLook);
    }

    /// <summary>
    /// One line per file: path, then a hash over the arena tree the normal parse produces
    /// (kind, type name, range, parent, named flag and chain head of every node in pre-order).
    /// Comparing two runs proves a grammar or builder change left the trees untouched.
    /// </summary>
    public static void WriteTreeHashes(ILanguage language, IReadOnlyList<string> files, string path)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            if (!SourceFile.TryRead(file, out var source, out _))
            {
                continue;
            }

            var tree = language.Parse(source!.Text).Tree;
            var sb = new StringBuilder();
            for (var i = 0; i < tree.NodeCount; i++)
            {
                ref readonly var n = ref tree[i];
                sb.Append((int)n.Kind)
                    .Append('|')
                    .Append(n.TypeName)
                    .Append('|')
                    .Append(n.Start)
                    .Append('|')
                    .Append(n.End)
                    .Append('|')
                    .Append(n.Parent)
                    .Append('|')
                    .Append(n.IsNamed ? '1' : '0')
                    .Append('|')
                    .Append(n.ChainHead)
                    .Append('\n');
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
            writer.WriteLine($"{file}\t{tree.NodeCount}\t{hash}");
        }
    }

    private static CommonTokenStream Lex(IAntlrLanguage language, string text)
    {
        var lexer = language.CreateLexer(CharStreams.fromString(text));
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();
        return tokens;
    }

    /// <summary>Runs SLL + bail like the production parser; returns the bail site or null on success.</summary>
    private static string? TrySll(IAntlrLanguage language, CommonTokenStream tokens)
    {
        var parser = language.CreateParser(tokens);
        parser.RemoveErrorListeners();
        var errors = new CountingErrorListener();
        parser.AddErrorListener(errors);
        parser.Interpreter.PredictionMode = PredictionMode.SLL;
        parser.ErrorHandler = new BailErrorStrategy();
        try
        {
            language.InvokeRule(parser, language.StartRule);
            return errors.Count == 0 ? null : "(error reported without bail)";
        }
        catch (ParseCanceledException ex) when (ex.InnerException is RecognitionException re)
        {
            return DescribeSite(language, parser, re);
        }
        catch (RecognitionException re)
        {
            return DescribeSite(language, parser, re);
        }
    }

    private static string DescribeSite(IAntlrLanguage language, Parser parser, RecognitionException re)
    {
        var stack = new List<string>();
        for (var ctx = re.Context; ctx is not null && stack.Count < 6; ctx = ctx.Parent)
        {
            if (ctx.RuleIndex >= 0 && ctx.RuleIndex < language.RuleNames.Count)
            {
                stack.Add(language.RuleNames[ctx.RuleIndex]);
            }
        }

        // The offending state is the decision state for NoViableAlt (generated code sets State
        // right before AdaptivePredict), so it names the alternative SLL could not choose.
        var state = re.OffendingState;
        var atn = parser.Interpreter.atn;
        var stateLabel = $"s{state}";
        if (state >= 0 && state < atn.states.Count && atn.states[state] is DecisionState ds && ds.decision >= 0)
        {
            stateLabel += $"/d{ds.decision}";
        }

        var token = re.OffendingToken;
        var tokenLabel = token is null ? "?" : language.Vocabulary.GetDisplayName(token.Type);
        return $"{re.GetType().Name} {string.Join(" < ", stack)} @{stateLabel} {tokenLabel}";
    }

    private static void WriteDecisionTable(
        TextWriter output,
        string title,
        DecisionTotals[] totals,
        ATN atn,
        IAntlrLanguage language,
        int top,
        Func<DecisionTotals, long> key
    )
    {
        output.WriteLine();
        output.WriteLine($"{title} (top {top}):");
        output.WriteLine(
            "  decision  rule                          time_ms  invocations  ll_fallback  ll_maxlook  ambiguities"
        );
        var rows = Enumerable
            .Range(0, totals.Length)
            .Where(d => key(totals[d]) > 0)
            .OrderByDescending(d => key(totals[d]))
            .Take(top);
        foreach (var d in rows)
        {
            var t = totals[d];
            var ruleIndex = atn.decisionToState[d].ruleIndex;
            var rule = ruleIndex >= 0 && ruleIndex < language.RuleNames.Count ? language.RuleNames[ruleIndex] : "?";
            output.WriteLine(
                $"  d{d, -7}  {rule, -28}  {t.Time100Ns / 1e4, 7:F1}  {t.Invocations, 11}  {t.LlFallback, 11}  {t.LlMaxLook, 10}  {t.Ambiguities, 11}"
            );
        }
    }

    private struct DecisionTotals
    {
        // ProfilingATNSimulator measures with DateTime.ToFileTime, i.e. 100ns units.
        public long Time100Ns;
        public long Invocations;
        public long LlFallback;
        public long LlMaxLook;
        public long Ambiguities;

        public void Add(DecisionInfo info)
        {
            Time100Ns += info.timeInPrediction;
            Invocations += info.invocations;
            LlFallback += info.LL_Fallback;
            LlMaxLook = Math.Max(LlMaxLook, info.LL_MaxLook);
            Ambiguities += info.ambiguities.Count;
        }
    }

    private sealed class CountingErrorListener : BaseErrorListener
    {
        public int Count { get; private set; }

        public override void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e
        ) => Count++;
    }
}
