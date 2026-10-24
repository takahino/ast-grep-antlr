namespace AstGrepAntlr.Language;

/// <summary>Registry of built-in and custom languages.</summary>
public sealed class LanguageRegistry
{
    private readonly Dictionary<string, ILanguage> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<ILanguage>> _lazy = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Register(ILanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        lock (_gate)
        {
            _lazy.Remove(language.Id);
            _byId[language.Id] = language;
        }
    }

    /// <summary>
    /// Registers a language factory that is only invoked on first <see cref="Get"/>.
    /// Used so <c>run -l java</c> does not compile unrelated custom grammars from sgconfig.
    /// </summary>
    public void RegisterLazy(string id, Func<ILanguage> factory)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            _byId.Remove(id);
            _lazy[id] = factory;
        }
    }

    /// <summary>Maps an alternate id (e.g. <c>c++</c>) to an already registered language.</summary>
    public void RegisterAlias(string alias, string languageId)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(alias);
        ArgumentNullException.ThrowIfNullOrEmpty(languageId);
        lock (_gate)
        {
            if (_byId.TryGetValue(languageId, out var language))
            {
                _byId[alias] = language;
                return;
            }

            if (_lazy.ContainsKey(languageId))
            {
                _lazy[alias] = () => Get(languageId);
                return;
            }

            throw new KeyNotFoundException($"Cannot register alias '{alias}': unknown language '{languageId}'.");
        }
    }

    public ILanguage Get(string id)
    {
        Func<ILanguage>? factory;
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var lang))
            {
                return lang;
            }

            if (!_lazy.TryGetValue(id, out factory))
            {
                var known = _byId.Keys.Concat(_lazy.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order();
                throw new KeyNotFoundException($"Unknown language '{id}'. Known: {string.Join(", ", known)}");
            }
        }

        // Invoke outside the lock so nested Get (aliases) and long grammar compiles do not deadlock.
        var created = factory();
        lock (_gate)
        {
            _byId[id] = created;
            _lazy.Remove(id);
            return created;
        }
    }

    public IReadOnlyCollection<ILanguage> All
    {
        get
        {
            List<string> pending;
            lock (_gate)
            {
                pending = _lazy.Keys.ToList();
            }

            foreach (var id in pending)
            {
                Get(id);
            }

            lock (_gate)
            {
                return _byId.Values.ToList();
            }
        }
    }
}
