using AstGrepAntlr.Language;
using AstGrepAntlr.Matching;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Pattern;
using AstGrepAntlr.Rules;

namespace AstGrepAntlr.Search;

public sealed class SearchOptions
{
    public required LanguageRegistry Registry { get; init; }
    public string? LanguageId { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = ["."];
    public int MaxResults { get; init; } = int.MaxValue;
    public bool ShowErrors { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class Searcher
{
    private readonly TwoStageParser _parser = new();
    private readonly PatternCompiler _compiler = new();
    private readonly PatternMatcher _matcher = new();
    private readonly RuleEngine _rules = new();
    private readonly FileWalker _walker = new();

    public IEnumerable<MatchResult> RunPattern(
        string pattern,
        SearchOptions options,
        Strictness strictness = Strictness.Smart,
        string? entry = null
    )
    {
        var langId = options.LanguageId ?? throw new ArgumentException("Language is required for pattern search (-l).");
        var language = options.Registry.Get(langId);
        var compiled = _compiler.Compile(language, new PatternSpec(pattern, entry, Strictness: strictness));
        var files = _walker.Enumerate(options.Paths, language.Extensions);
        return SearchFiles(files, language, compiled, options, ruleId: null, message: null, severity: null);
    }

    public IEnumerable<MatchResult> RunRule(SgRuleDocument doc, SearchOptions options)
    {
        if (doc.Language is null || doc.Rule is null)
        {
            yield break;
        }

        var language = options.Registry.Get(doc.Language);
        var files = _walker.Enumerate(options.Paths, language.Extensions, ignores: doc.Ignores);
        var count = 0;
        foreach (var file in files)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (language.MaxFileBytes > 0)
            {
                var fi = new FileInfo(file);
                if (fi.Length > language.MaxFileBytes)
                {
                    continue;
                }
            }

            var text = File.ReadAllText(file);
            // Prefilter when pattern present
            if (
                doc.Rule.Pattern is not null
                && !Prefilter.MayContain(text, ExtractLiterals(doc.Rule.Pattern), language.IgnoreCase)
            )
            {
                continue;
            }

            ParseResult parsed;
            try
            {
                parsed = _parser.Parse(
                    language,
                    text,
                    new LanguageParseOptions { CancellationToken = options.CancellationToken }
                );
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var (node, env) in _rules.Match(language, parsed.Tree, doc))
            {
                yield return ToMatch(file, language.Id, parsed.Tree, node, env, doc.Id, doc.Message, doc.Severity);
                if (++count >= options.MaxResults)
                {
                    yield break;
                }
            }
        }
    }

    private IEnumerable<MatchResult> SearchFiles(
        IEnumerable<string> files,
        ILanguage language,
        CompiledPattern compiled,
        SearchOptions options,
        string? ruleId,
        string? message,
        string? severity
    )
    {
        var count = 0;
        var fileList = files.ToList();
        var bag = new System.Collections.Concurrent.ConcurrentBag<(string File, MatchResult Match)>();

        Parallel.ForEach(
            fileList,
            new ParallelOptions
            {
                CancellationToken = options.CancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            },
            file =>
            {
                if (language.MaxFileBytes > 0 && new FileInfo(file).Length > language.MaxFileBytes)
                {
                    return;
                }

                // ReadAllText strips BOM; do not use UTF8.GetString(ReadAllBytes) which keeps U+FEFF.
                var text = File.ReadAllText(file);
                if (!Prefilter.MayContain(text, compiled.RequiredLiterals, language.IgnoreCase))
                {
                    return;
                }

                ParseResult parsed;
                try
                {
                    parsed = _parser.Parse(
                        language,
                        text,
                        new LanguageParseOptions { CancellationToken = options.CancellationToken }
                    );
                }
                catch (Exception ex)
                {
                    if (options.ShowErrors)
                    {
                        Console.Error.WriteLine($"ag4: parse failed {file}: {ex.Message}");
                    }

                    return;
                }

                foreach (var (node, env) in _matcher.MatchAll(parsed.Tree, compiled))
                {
                    bag.Add((file, ToMatch(file, language.Id, parsed.Tree, node, env, ruleId, message, severity)));
                }
            }
        );

        foreach (var item in bag.OrderBy(x => x.File).ThenBy(x => x.Match.Start))
        {
            yield return item.Match;
            if (++count >= options.MaxResults)
            {
                yield break;
            }
        }
    }

    private static MatchResult ToMatch(
        string file,
        string lang,
        Tree.SyntaxTree tree,
        int node,
        Dictionary<string, string> env,
        string? ruleId,
        string? message,
        string? severity
    )
    {
        var n = tree[node];
        var (sl, sc) = OffsetToLineCol(tree.Source, n.Start);
        var (el, ec) = OffsetToLineCol(tree.Source, n.End);
        return new MatchResult
        {
            File = file,
            Language = lang,
            Start = n.Start,
            End = n.End,
            StartLine = sl,
            StartColumn = sc,
            EndLine = el,
            EndColumn = ec,
            Text = tree.Text(node),
            MetaVariables = env,
            RuleId = ruleId,
            Message = message,
            Severity = severity,
        };
    }

    private static (int Line, int Col) OffsetToLineCol(string source, int offset)
    {
        var line = 1;
        var col = 1;
        offset = Math.Clamp(offset, 0, source.Length);
        for (var i = 0; i < offset; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }

    private static List<string> ExtractLiterals(string pattern)
    {
        var occ = MetaVar.FindAll(pattern);
        var text = pattern;
        for (var i = occ.Count - 1; i >= 0; i--)
        {
            text = string.Concat(text.AsSpan(0, occ[i].Start), " ", text.AsSpan(occ[i].Start + occ[i].Length));
        }

        return text.Split(
                [' ', '(', ')', '{', '}', '[', ']', '.', ',', ';', '+', '-', '*', '/', '=', '<', '>', '!'],
                StringSplitOptions.RemoveEmptyEntries
            )
            .Where(t => t.Length >= 2 && t.All(char.IsLetterOrDigit) || t.Contains('_'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
