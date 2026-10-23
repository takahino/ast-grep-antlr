namespace AstGrepAntlr.Tree;

/// <summary>Arena-backed syntax tree with kind indexes for fast candidate lookup.</summary>
public sealed class SyntaxTree
{
    private readonly SyntaxNode[] _nodes;
    private readonly Dictionary<string, List<int>> _kindIndex;
    private readonly string[] _chainKinds;

    public SyntaxTree(
        string source,
        string languageId,
        SyntaxNode[] nodes,
        Dictionary<string, List<int>> kindIndex,
        string[] chainKinds
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

        // Walk the collapsed single-child chain stored as contiguous TypeName via ChainKinds encoding:
        // ChainKinds[i] is the joined "a>b>c" for chain head i when available; else match TypeName.
        if ((uint)head < (uint)_chainKinds.Length && !string.IsNullOrEmpty(_chainKinds[head]))
        {
            foreach (var part in _chainKinds[head].Split('>'))
            {
                if (string.Equals(part, kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return string.Equals(_nodes[index].TypeName, kind, StringComparison.Ordinal);
    }

    public IEnumerable<int> Descendants(int index, bool namedOnly = false)
    {
        var stack = new Stack<int>();
        foreach (var c in Children(index).Reverse())
        {
            stack.Push(c);
        }

        while (stack.Count > 0)
        {
            var i = stack.Pop();
            if (!namedOnly || _nodes[i].IsNamed)
            {
                yield return i;
            }

            foreach (var c in Children(i).Reverse())
            {
                stack.Push(c);
            }
        }
    }
}
