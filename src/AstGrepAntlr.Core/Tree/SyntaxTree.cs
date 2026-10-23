namespace AstGrepAntlr.Tree;

/// <summary>Arena-backed syntax tree with kind indexes for fast candidate lookup.</summary>
public sealed class SyntaxTree
{
    private readonly SyntaxNode[] _nodes;
    private readonly Dictionary<string, List<int>> _kindIndex;
    private readonly string[][] _chainKinds;

    public SyntaxTree(
        string source,
        string languageId,
        SyntaxNode[] nodes,
        Dictionary<string, List<int>> kindIndex,
        string[][] chainKinds
    )
    {
        Source = source;
        LanguageId = languageId;
        _nodes = nodes;
        _kindIndex = kindIndex;
        _chainKinds = chainKinds;
        Root = nodes.Length > 0 ? 0 : -1;
    }

    /// <summary>Mark a node as a meta-variable (used by pattern compilation).</summary>
    public void SetMetaVar(int index, string name, bool isMulti)
    {
        var n = _nodes[index];
        _nodes[index] = n with { MetaVarName = name, IsMultiMetaVar = isMulti };
    }

    public string Source { get; }
    public string LanguageId { get; }
    public int Root { get; }
    public int NodeCount => _nodes.Length;

    public ref readonly SyntaxNode this[int index] => ref _nodes[index];

    public string Text(int index)
    {
        var n = _nodes[index];
        if (n.Start < 0 || n.End < n.Start || n.End > Source.Length)
        {
            return string.Empty;
        }

        return Source[n.Start..n.End];
    }

    public IEnumerable<int> Children(int index)
    {
        var n = _nodes[index];
        var child = n.FirstChild;
        while (child >= 0)
        {
            yield return child;
            child = _nodes[child].NextSibling;
        }
    }

    public IEnumerable<int> NamedChildren(int index)
    {
        foreach (var c in Children(index))
        {
            if (_nodes[c].IsNamed)
            {
                yield return c;
            }
        }
    }

    public IReadOnlyList<int> NodesOfKind(string kind)
    {
        return _kindIndex.TryGetValue(kind, out var list) ? list : Array.Empty<int>();
    }

    /// <summary>
    /// True when the node is of <paramref name="kind"/>, counting the rule names that were
    /// collapsed into its single-child chain so `expression` still matches a chain head.
    /// </summary>
    public bool KindMatches(int index, string kind)
    {
        if (string.Equals(kind, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return _nodes[index].IsError;
        }

        var head = _nodes[index].ChainHead;
        if (head < 0)
        {
            head = index;
        }

        if ((uint)head < (uint)_chainKinds.Length)
        {
            foreach (var part in _chainKinds[head])
            {
                if (string.Equals(part, kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return string.Equals(_nodes[index].TypeName, kind, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every node of <paramref name="kind"/>. Scanning with <see cref="KindMatches"/> already
    /// covers the kind index, since each node is indexed under its own TypeName.
    /// </summary>
    public IEnumerable<int> NodesMatchingKind(string kind)
    {
        for (var i = 0; i < _nodes.Length; i++)
        {
            if (KindMatches(i, kind))
            {
                yield return i;
            }
        }
    }

    /// <summary>Depth-first pre-order descendants of <paramref name="index"/>, excluding the node itself.</summary>
    public IEnumerable<int> Descendants(int index)
    {
        var first = _nodes[index].FirstChild;
        if (first < 0)
        {
            yield break;
        }

        // Children are a sibling-linked list, so pushing (sibling, then child) walks pre-order
        // while holding at most one pending node per depth level — no per-node buffering.
        var stack = new Stack<int>();
        stack.Push(first);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            yield return i;

            var sibling = _nodes[i].NextSibling;
            if (sibling >= 0)
            {
                stack.Push(sibling);
            }

            var child = _nodes[i].FirstChild;
            if (child >= 0)
            {
                stack.Push(child);
            }
        }
    }
}
