namespace AstGrepAntlr.Language;

/// <summary>Registry of built-in and custom languages.</summary>
public sealed class LanguageRegistry
{
    private readonly Dictionary<string, ILanguage> _byId = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ILanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        _byId[language.Id] = language;
    }

    public ILanguage Get(string id) =>
        _byId.TryGetValue(id, out var lang)
            ? lang
            : throw new KeyNotFoundException(
                $"Unknown language '{id}'. Known: {string.Join(", ", _byId.Keys.Order())}"
            );

    public IReadOnlyCollection<ILanguage> All => _byId.Values;
}
