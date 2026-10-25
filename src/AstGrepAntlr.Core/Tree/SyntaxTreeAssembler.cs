namespace AstGrepAntlr.Tree;

/// <summary>
/// Builds an arena <see cref="SyntaxTree"/>: sibling links, kind index, optional kind aliases,
/// and single-child rule chain collapse.
/// </summary>
public sealed class SyntaxTreeAssembler
{
    private readonly List<SyntaxNode> _nodes = [];
    private readonly Dictionary<string, List<int>> _kindIndex = new(StringComparer.Ordinal);
    private readonly List<string[]> _extraKinds = [];
    private readonly List<string[]> _chainKinds = [];
    private readonly Dictionary<string, string[]> _singleKinds = new(StringComparer.Ordinal);
    private readonly bool _collapseChains;

    /// <param name="collapseChains">
    /// When true (default), collapse single-child rule chains — useful for deep ANTLR CSTs.
    /// tree-sitter trees are already flatter; pass false to keep node kinds exact.
    /// </param>
    public SyntaxTreeAssembler(bool collapseChains = true)
    {
        _collapseChains = collapseChains;
    }

    public int Count => _nodes.Count;

    /// <summary>Reserve a slot; call <see cref="Commit"/> after children are linked.</summary>
    public int Begin()
    {
        var index = _nodes.Count;
        _nodes.Add(default);
        _extraKinds.Add([]);
        _chainKinds.Add([]);
        return index;
    }

    /// <summary>
    /// Finalize a node. <paramref name="aliases"/> are registered in the kind index and
    /// merged into the collapsed chain so <see cref="SyntaxTree.KindMatches"/> finds them.
    /// </summary>
    public void Commit(int index, SyntaxNode node, IReadOnlyList<string>? aliases = null)
    {
        var aliasArr = aliases is { Count: > 0 } ? aliases.ToArray() : [];
        _extraKinds[index] = aliasArr;
        _nodes[index] = node with { Index = index, ChainHead = index, NextSibling = node.NextSibling };
        AddKind(_kindIndex, node.TypeName, index);
        foreach (var alias in aliasArr)
        {
            AddKind(_kindIndex, alias, index);
        }

        if (node.Kind is SyntaxNodeKind.Error or SyntaxNodeKind.Missing)
        {
            AddKind(_kindIndex, "ERROR", index);
        }
    }

    public void SetNextSibling(int index, int nextSibling)
    {
        var n = _nodes[index];
        _nodes[index] = n with { NextSibling = nextSibling };
    }

    public SyntaxTree Build(string source, string languageId)
    {
        if (_collapseChains)
        {
            CollapseChains();
        }
        else
        {
            // Still seed chainKinds with each node's own TypeName (+ aliases) so KindMatches works.
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_extraKinds[i].Length == 0)
                {
                    _chainKinds[i] = SingleKind(_nodes[i].TypeName);
                    continue;
                }

                var parts = new List<string>();
                AddUnique(parts, _nodes[i].TypeName);
                foreach (var alias in _extraKinds[i])
                {
                    AddUnique(parts, alias);
                }

                _chainKinds[i] = parts.ToArray();
            }
        }

        return new SyntaxTree(source, languageId, _nodes.ToArray(), _kindIndex, _chainKinds.ToArray());
    }

    public static void AddKind(Dictionary<string, List<int>> kindIndex, string kind, int index)
    {
        if (!kindIndex.TryGetValue(kind, out var list))
        {
            list = [];
            kindIndex[kind] = list;
        }

        list.Add(index);
    }

    private void CollapseChains()
    {
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Kind != SyntaxNodeKind.Rule || _nodes[i].ChainHead != i)
            {
                continue;
            }

            // Most heads have no alias and no rule child to absorb; they need no list of their own.
            if (_extraKinds[i].Length == 0 && !ContinuesChain(i))
            {
                _chainKinds[i] = SingleKind(_nodes[i].TypeName);
                continue;
            }

            var parts = new List<string>();
            AddUnique(parts, _nodes[i].TypeName);
            foreach (var alias in _extraKinds[i])
            {
                AddUnique(parts, alias);
            }

            var current = i;
            while (ContinuesChain(current))
            {
                var child = _nodes[current].FirstChild;
                AddUnique(parts, _nodes[child].TypeName);
                foreach (var alias in _extraKinds[child])
                {
                    AddUnique(parts, alias);
                }

                _nodes[child] = _nodes[child] with { ChainHead = i };
                current = child;
            }

            _nodes[i] = _nodes[i] with { ChainHead = i };
            // Stored pre-split: KindMatches runs over every node of every tree, so splitting a
            // joined string there would allocate on the hottest path in the matcher.
            _chainKinds[i] = parts.ToArray();
        }
    }

    private bool ContinuesChain(int index)
    {
        var n = _nodes[index];
        return n.ChildCount == 1 && n.FirstChild >= 0 && _nodes[n.FirstChild].Kind == SyntaxNodeKind.Rule;
    }

    /// <summary>
    /// One shared array per kind name within this tree; chain kinds are read-only after
    /// <see cref="Build"/>, so heads of the same kind can share it.
    /// </summary>
    private string[] SingleKind(string typeName)
    {
        if (!_singleKinds.TryGetValue(typeName, out var arr))
        {
            arr = [typeName];
            _singleKinds[typeName] = arr;
        }

        return arr;
    }

    // List<string>.Contains compares with the default (ordinal) string equality, without LINQ.
    private static void AddUnique(List<string> parts, string name)
    {
        if (!parts.Contains(name))
        {
            parts.Add(name);
        }
    }
}
