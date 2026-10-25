using System.Diagnostics;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.CSharp;
using AstGrepAntlr.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ArenaTree = AstGrepAntlr.Tree.SyntaxTree;
using RoslynNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace AstGrepAntlr.Languages;

/// <summary>C# language front-end backed by Roslyn (Microsoft.CodeAnalysis.CSharp).</summary>
public sealed class CSharpLanguage : ILanguage
{
    private static readonly IReadOnlyList<string> PatternRuleList =
    [
        "expression",
        "statement",
        "member_declaration",
        "type",
        "compilation_unit",
    ];

    private static readonly IReadOnlyList<string> KindNames = RoslynKindNames.AllSnakeCaseNames();

    private readonly RoslynTreeBuilder _builder = new();

    public string Id => "csharp";
    public IReadOnlyList<string> Extensions { get; } = [".cs"];
    public string StartRule => "compilation_unit";
    public IReadOnlyList<string> PatternRules => PatternRuleList;
    public string MetaVarPlaceholderTemplate => "__MV{0}__";

    public IReadOnlyList<string> MetaVarVariants { get; } =
    ["{0}", "{0};", "", "int {0}", "{0} {0}", "{0} {0};", "int {0};", "void {0}(){}", ";"];

    public bool IgnoreCase => false;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 8 * 1024 * 1024;
    public IReadOnlyList<string> RuleNames => KindNames;

    public ParseResult Parse(string source, LanguageParseOptions? options = null)
    {
        options ??= new LanguageParseOptions();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
        if (ParseTimeout > TimeSpan.Zero)
        {
            cts.CancelAfter(ParseTimeout);
        }

        var timings = options.Timings;
        Microsoft.CodeAnalysis.SyntaxTree tree;
        CompilationUnitSyntax root;
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None),
                cancellationToken: cts.Token
            );
            root = tree.GetCompilationUnitRoot(cts.Token);
            timings.AddParse(sw.Elapsed);
        }
        else
        {
            tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None),
                cancellationToken: cts.Token
            );
            root = tree.GetCompilationUnitRoot(cts.Token);
        }

        ArenaTree syntax;
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            syntax = _builder.Build(source, Id, root);
            timings.AddBuild(sw.Elapsed);
        }
        else
        {
            syntax = _builder.Build(source, Id, root);
        }

        List<ParseDiagnostic> diagnostics;
        if (timings is not null)
        {
            var sw = Stopwatch.StartNew();
            diagnostics = tree.GetDiagnostics(cts.Token)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(ToDiagnostic)
                .ToList();
            timings.AddDiagnostics(sw.Elapsed);
        }
        else
        {
            diagnostics = tree.GetDiagnostics(cts.Token)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(ToDiagnostic)
                .ToList();
        }

        var entry = options.EntryRule ?? StartRule;
        return new ParseResult(syntax, diagnostics, entry, UsedLlFallback: false);
    }

    public ArenaTree? TryParseFragment(string source, string rule)
    {
        try
        {
            var node = ParseFragmentNode(source, rule);
            if (node is null)
            {
                return null;
            }

            if (node.ContainsDiagnostics && node.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }

            // Full-text consume: node span must cover the trimmed source (allow leading/trailing trivia/ws).
            var trimmedStart = IndexOfNonWhiteSpace(source, 0);
            var trimmedEnd = LastIndexOfNonWhiteSpace(source) + 1;
            if (trimmedStart < trimmedEnd)
            {
                if (node.SpanStart > trimmedStart || node.Span.End < trimmedEnd)
                {
                    // CompilationUnit always spans the whole text; other parsers may leave trailing junk.
                    if (!string.Equals(rule, "compilation_unit", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }

            return _builder.Build(source, Id, node);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static RoslynNode? ParseFragmentNode(string source, string rule)
    {
        return rule.ToLowerInvariant() switch
        {
            "expression" => Try(() =>
                SyntaxFactory.ParseExpression(source, options: ParseOpts(), consumeFullText: true)
            ),
            "statement" => Try(() => SyntaxFactory.ParseStatement(source, options: ParseOpts(), consumeFullText: true)),
            "member_declaration" or "memberdeclaration" => Try(() =>
                SyntaxFactory.ParseMemberDeclaration(source, options: ParseOpts())
            ),
            "type" or "typename" => Try(() =>
                SyntaxFactory.ParseTypeName(source, options: ParseOpts(), consumeFullText: true)
            ),
            "compilation_unit" or "compilationunit" => CSharpSyntaxTree
                .ParseText(source, ParseOpts())
                .GetCompilationUnitRoot(),
            _ => null,
        };

        static RoslynNode? Try(Func<RoslynNode?> f)
        {
            try
            {
                return f();
            }
            catch
            {
                return null;
            }
        }
    }

    private static CSharpParseOptions ParseOpts() => new(LanguageVersion.Latest, DocumentationMode.None);

    private static ParseDiagnostic ToDiagnostic(Diagnostic d)
    {
        var lineSpan = d.Location.GetLineSpan();
        return new ParseDiagnostic(
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character,
            d.GetMessage(),
            IsLexer: false
        );
    }

    private static int IndexOfNonWhiteSpace(string s, int start)
    {
        for (var i = start; i < s.Length; i++)
        {
            if (!char.IsWhiteSpace(s[i]))
            {
                return i;
            }
        }

        return s.Length;
    }

    private static int LastIndexOfNonWhiteSpace(string s)
    {
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(s[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
