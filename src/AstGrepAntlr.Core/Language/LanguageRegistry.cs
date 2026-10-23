namespace AstGrepAntlr.Language;

/// <summary>Registry of built-in and custom languages.</summary>
public sealed class LanguageRegistry
{
    private readonly Dictionary<string, ILanguage> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILanguage> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ILanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        _byId[language.Id] = language;
        foreach (var ext in language.Extensions)
        {
            var key = ext.StartsWith('.') ? ext : "." + ext;
            _byExtension[key] = language;
        }
    }

    public ILanguage Get(string id) =>
        _byId.TryGetValue(id, out var lang)
            ? lang
            : throw new KeyNotFoundException(
                $"Unknown language '{id}'. Known: {string.Join(", ", _byId.Keys.Order())}"
            );

    public bool TryGet(string id, out ILanguage language) => _byId.TryGetValue(id, out language!);

    public ILanguage? FromPath(string path)
    {
        var ext = Path.GetExtension(path);
        return _byExtension.TryGetValue(ext, out var lang) ? lang : null;
    }

    public IReadOnlyCollection<ILanguage> All => _byId.Values;
}
