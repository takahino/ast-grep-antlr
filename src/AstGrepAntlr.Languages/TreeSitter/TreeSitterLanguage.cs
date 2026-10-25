using System.Collections.Concurrent;
using AstGrepAntlr.Language;
using AstGrepAntlr.Parsing;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Languages.TreeSitter;

/// <summary>tree-sitter-backed language front-end producing arena <see cref="SyntaxTree"/>s.</summary>
public sealed class TreeSitterLanguage : ILanguage
{
    private readonly nint _language;
    private readonly TreeSitterTreeBuilder _builder;
    private readonly IReadOnlyList<PatternContext> _contexts;
    private readonly ConcurrentDictionary<int, nint> _parsers = new();
    private readonly string _libraryStem;
    private static int s_probeOk = -1;

    public TreeSitterLanguage(
        string id,
        IReadOnlyList<string> extensions,
        string libraryStem,
        string exportName,
        string startRule,
        IReadOnlyList<PatternContext> contexts,
        IReadOnlyList<string>? metaVarVariants = null,
        string metaVarPlaceholderTemplate = "__MV{0}__",
        bool ignoreCase = false
    )
    {
        TsNative.EnsureResolver();
        Id = id;
        Extensions = extensions;
        StartRule = startRule;
        _libraryStem = libraryStem;
        _language = TsNative.LoadLanguage(libraryStem, exportName);
        _builder = new TreeSitterTreeBuilder(_language);
        _contexts = contexts;
        PatternRules = contexts.Select(c => c.Name).ToList();
        MetaVarPlaceholderTemplate = metaVarPlaceholderTemplate;
        MetaVarVariants = metaVarVariants ?? ["{0}"];
        IgnoreCase = ignoreCase;
        RuleNames = _builder.RuleNames;
    }

    public string Id { get; }
    public IReadOnlyList<string> Extensions { get; }
    public string StartRule { get; }
    public IReadOnlyList<string> PatternRules { get; }
    public string MetaVarPlaceholderTemplate { get; }
    public IReadOnlyList<string> MetaVarVariants { get; }
    public bool IgnoreCase { get; }
    public TimeSpan ParseTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public long MaxFileBytes { get; init; } = 8 * 1024 * 1024;
    public IReadOnlyList<string> RuleNames { get; }

    /// <summary>True when the tree-sitter core + at least one grammar native can be loaded.</summary>
    public static bool NativesAvailable
    {
        get
        {
            var cached = Volatile.Read(ref s_probeOk);
            if (cached >= 0)
            {
                return cached == 1;
            }

            try
            {
                TsNative.EnsureResolver();
                var p = TsNative.ts_parser_new();
                if (p == nint.Zero)
                {
                    Volatile.Write(ref s_probeOk, 0);
                    return false;
                }

                TsNative.ts_parser_delete(p);
                Volatile.Write(ref s_probeOk, 1);
                return true;
            }
            catch
            {
                Volatile.Write(ref s_probeOk, 0);
                return false;
            }
        }
    }

    public ParseResult Parse(string source, LanguageParseOptions? options = null)
    {
        options ??= new LanguageParseOptions();
        options.CancellationToken.ThrowIfCancellationRequested();

        var parser = GetThreadParser();
        var treePtr = TsNative.ts_parser_parse_string_encoding(
            parser,
            nint.Zero,
            source,
            (uint)source.Length * 2,
            TsNative.InputEncoding.Utf16Le
        );
        if (treePtr == nint.Zero)
        {
            throw new InvalidOperationException($"tree-sitter failed to parse {Id} source.");
        }

        try
        {
            var root = TsNative.ts_tree_root_node(treePtr);
            var syntax = _builder.Build(source, Id, root);
            var diagnostics = CollectDiagnostics(root, source);
            return new ParseResult(syntax, diagnostics, options.EntryRule ?? StartRule, UsedLlFallback: false);
        }
        finally
        {
            TsNative.ts_tree_delete(treePtr);
        }
    }

    public SyntaxTree? TryParseFragment(string source, string rule)
    {
        var ctx = _contexts.FirstOrDefault(c => string.Equals(c.Name, rule, StringComparison.OrdinalIgnoreCase));
        if (ctx is null)
        {
            // Allow StartRule / root kind as bare parse.
            if (string.Equals(rule, StartRule, StringComparison.OrdinalIgnoreCase))
            {
                ctx = new PatternContext(StartRule, "{0}");
            }
            else
            {
                return null;
            }
        }

        var wrapped = ctx.Wrap(source);
        var embedStart = ctx.EmbedStart(source);
        var embedEnd = embedStart + source.Length;

        var parser = GetThreadParser();
        var treePtr = TsNative.ts_parser_parse_string_encoding(
            parser,
            nint.Zero,
            wrapped,
            (uint)wrapped.Length * 2,
            TsNative.InputEncoding.Utf16Le
        );
        if (treePtr == nint.Zero)
        {
            return null;
        }

        try
        {
            var root = TsNative.ts_tree_root_node(treePtr);
            if (HasDisallowedErrorOrMissing(root, embedEnd))
            {
                return null;
            }

            // Prefer exact span; allow a covering node only when its span matches the embed after
            // trimming trailing newlines (preprocessor / module wraps).
            var target = FindExactSpan(root, embedStart, embedEnd);
            if (TsNative.ts_node_is_null(target))
            {
                target = FindCoveringTrimmed(root, embedStart, embedEnd);
            }

            if (TsNative.ts_node_is_null(target))
            {
                return null;
            }

            // Reject wraps that only "succeed" by inventing MISSING tokens inside the fragment
            // (e.g. expression context turning `return $E;` into a bogus qualified_identifier).
            if (SubtreeHasDisallowedMissing(target, embedEnd))
            {
                return null;
            }

            if (IsExpressionInitContext(ctx.Name) && IsStatementOrDeclarationKind(SymbolName(target)))
            {
                return null;
            }

            return _builder.BuildShifted(source, Id, target, embedStart, embedEnd);
        }
        finally
        {
            TsNative.ts_tree_delete(treePtr);
        }
    }

    private nint GetThreadParser()
    {
        var key = Environment.CurrentManagedThreadId;
        return _parsers.GetOrAdd(
            key,
            _ =>
            {
                var p = TsNative.ts_parser_new();
                if (p == nint.Zero || !TsNative.ts_parser_set_language(p, _language))
                {
                    throw new InvalidOperationException($"Failed to create tree-sitter parser for {_libraryStem}.");
                }

                return p;
            }
        );
    }

    private static List<ParseDiagnostic> CollectDiagnostics(TsNative.TSNode root, string source)
    {
        var list = new List<ParseDiagnostic>();
        WalkErrors(root, source, list);
        return list;
    }

    private static void WalkErrors(TsNative.TSNode node, string source, List<ParseDiagnostic> list)
    {
        if (TsNative.ts_node_is_error(node) || TsNative.ts_node_is_missing(node))
        {
            var start = (int)(TsNative.ts_node_start_byte(node) / 2);
            var (line, col) = Locate(source, start);
            var kind = TsNative.ts_node_is_missing(node) ? "MISSING" : "ERROR";
            list.Add(new ParseDiagnostic(line, col, $"{kind} at {line}:{col}", IsLexer: false));
        }

        var n = TsNative.ts_node_child_count(node);
        for (uint i = 0; i < n; i++)
        {
            var child = TsNative.ts_node_child(node, i);
            if (!TsNative.ts_node_is_null(child))
            {
                WalkErrors(child, source, list);
            }
        }
    }

    private static (int Line, int Col) Locate(string source, int offset)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        var line = 1;
        var col = 1;
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

    /// <summary>
    /// Reject ERROR anywhere. Allow MISSING only at/after the embedded fragment end
    /// (typical trailing <c>;</c> inserted by an expression wrap).
    /// </summary>
    private static bool IsExpressionInitContext(string contextName) =>
        contextName is "expression" or "primary_expression";

    /// <summary>
    /// Kinds that belong in a statement/declaration wrap, not inside <c>T x = …</c>.
    /// </summary>
    private static bool IsStatementOrDeclarationKind(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return false;
        }

        return kind
            is "expression_statement"
                or "break_statement"
                or "continue_statement"
                or "return_statement"
                or "throw_statement"
                or "if_statement"
                or "while_statement"
                or "for_statement"
                or "enhanced_for_statement"
                or "try_statement"
                or "local_variable_declaration"
                or "local_variable_declaration_statement"
                or "local_declaration_statement"
                or "global_statement"
                or "declaration"
                or "function_definition"
                or "labeled_statement"
                or "switch_statement"
                or "do_statement"
                or "for_range_loop"
                or "foreach_statement"
                or "using_statement"
                or "lock_statement"
                or "fixed_statement"
                or "checked_statement"
                or "unchecked_statement"
                or "unsafe_statement";
    }

    private string SymbolName(TsNative.TSNode node)
    {
        var sym = TsNative.ts_node_symbol(node);
        return TsNative.MarshalAnsi(TsNative.ts_language_symbol_name(_language, sym)) ?? string.Empty;
    }

    private static bool HasDisallowedErrorOrMissing(TsNative.TSNode node, int embedEndUtf16)
    {
        if (TsNative.ts_node_is_error(node))
        {
            return true;
        }

        if (TsNative.ts_node_is_missing(node))
        {
            var start = (int)(TsNative.ts_node_start_byte(node) / 2);
            return start < embedEndUtf16;
        }

        var n = TsNative.ts_node_child_count(node);
        for (uint i = 0; i < n; i++)
        {
            var child = TsNative.ts_node_child(node, i);
            if (!TsNative.ts_node_is_null(child) && HasDisallowedErrorOrMissing(child, embedEndUtf16))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SubtreeHasDisallowedMissing(TsNative.TSNode node, int embedEndUtf16)
    {
        if (TsNative.ts_node_is_missing(node))
        {
            var start = (int)(TsNative.ts_node_start_byte(node) / 2);
            return start < embedEndUtf16;
        }

        var n = TsNative.ts_node_child_count(node);
        for (uint i = 0; i < n; i++)
        {
            var child = TsNative.ts_node_child(node, i);
            if (!TsNative.ts_node_is_null(child) && SubtreeHasDisallowedMissing(child, embedEndUtf16))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Smallest covering node whose span equals embedStart..embedEnd ignoring trailing newlines
    /// on the node end (common for translation_unit / preproc wraps).
    /// </summary>
    private static TsNative.TSNode FindCoveringTrimmed(TsNative.TSNode root, int startUtf16, int endUtf16)
    {
        var startByte = (uint)startUtf16 * 2;
        var endByte = (uint)endUtf16 * 2;
        TsNative.TSNode best = default;
        var found = false;
        Walk(
            root,
            n =>
            {
                var ns = TsNative.ts_node_start_byte(n);
                var ne = TsNative.ts_node_end_byte(n);
                if (ns != startByte || ne < endByte)
                {
                    return;
                }

                // Allow only trailing newline padding beyond embedEnd (0 or more \n, not other text).
                var extra = (int)((ne - endByte) / 2);
                if (extra > 2)
                {
                    return;
                }

                if (
                    !found
                    || TsNative.ts_node_end_byte(best) > ne
                    || (TsNative.ts_node_end_byte(best) == ne && TsNative.ts_node_is_named(n))
                )
                {
                    best = n;
                    found = true;
                }
            }
        );
        return found ? best : default;
    }

    private static TsNative.TSNode FindNodeCovering(TsNative.TSNode root, int startUtf16, int endUtf16)
    {
        var startByte = (uint)startUtf16 * 2;
        var endByte = (uint)endUtf16 * 2;
        var best = root;
        var cursor = TsNative.ts_tree_cursor_new(root);
        try
        {
            var stack = new Stack<bool>();
            while (true)
            {
                var node = TsNative.ts_tree_cursor_current_node(ref cursor);
                var ns = TsNative.ts_node_start_byte(node);
                var ne = TsNative.ts_node_end_byte(node);
                if (ns <= startByte && ne >= endByte)
                {
                    best = node;
                    if (TsNative.ts_tree_cursor_goto_first_child(ref cursor))
                    {
                        stack.Push(true);
                        continue;
                    }
                }

                if (TsNative.ts_tree_cursor_goto_next_sibling(ref cursor))
                {
                    continue;
                }

                var ascended = false;
                while (stack.Count > 0)
                {
                    stack.Pop();
                    if (!TsNative.ts_tree_cursor_goto_parent(ref cursor))
                    {
                        break;
                    }

                    if (TsNative.ts_tree_cursor_goto_next_sibling(ref cursor))
                    {
                        ascended = true;
                        break;
                    }
                }

                if (!ascended)
                {
                    break;
                }
            }
        }
        finally
        {
            TsNative.ts_tree_cursor_delete(ref cursor);
        }

        return best;
    }

    private static TsNative.TSNode FindExactSpan(TsNative.TSNode root, int startUtf16, int endUtf16)
    {
        var startByte = (uint)startUtf16 * 2;
        var endByte = (uint)endUtf16 * 2;
        TsNative.TSNode best = default;
        var found = false;
        Walk(
            root,
            n =>
            {
                if (TsNative.ts_node_start_byte(n) == startByte && TsNative.ts_node_end_byte(n) == endByte)
                {
                    // Prefer named / rule-like nodes.
                    if (!found || TsNative.ts_node_is_named(n) || TsNative.ts_node_child_count(n) > 0)
                    {
                        best = n;
                        found = true;
                    }
                }
            }
        );
        return found ? best : default;
    }

    private static void Walk(TsNative.TSNode node, Action<TsNative.TSNode> visit)
    {
        visit(node);
        var n = TsNative.ts_node_child_count(node);
        for (uint i = 0; i < n; i++)
        {
            var child = TsNative.ts_node_child(node, i);
            if (!TsNative.ts_node_is_null(child))
            {
                Walk(child, visit);
            }
        }
    }
}
