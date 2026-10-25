using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.TreeSitter;

namespace AstGrepAntlr.Languages;

public static class BuiltinLanguages
{
    /// <summary>Languages that can switch between tree-sitter and ANTLR.</summary>
    public static readonly IReadOnlyList<string> SwitchableIds = ["java", "cpp", "python"];

    public static LanguageRegistry CreateRegistry(LanguageBackend defaultBackend = LanguageBackend.TreeSitter)
    {
        var registry = new LanguageRegistry();
        RegisterFixed(registry);
        ApplyDefaultBackends(registry, defaultBackend);
        return registry;
    }

    /// <summary>
    /// Re-register <paramref name="languageId"/> with the chosen backend.
    /// Aliases (<c>c++</c>, <c>py</c>, …) are refreshed so they point at the new instance.
    /// </summary>
    public static void ApplyBackend(
        LanguageRegistry registry,
        string languageId,
        LanguageBackend backend,
        TextWriter? warn = null
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        var id = CanonicalId(languageId);
        if (!SwitchableIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Language '{languageId}' does not support backend switching. Switchable: {string.Join(", ", SwitchableIds)}."
            );
        }

        if (backend == LanguageBackend.TreeSitter && !TreeSitterLanguage.NativesAvailable)
        {
            warn?.WriteLine($"ag4: warning: tree-sitter natives unavailable; falling back to ANTLR for '{id}'.");
            backend = LanguageBackend.Antlr;
        }

        try
        {
            ILanguage lang = backend switch
            {
                LanguageBackend.TreeSitter => CreateTreeSitter(id),
                LanguageBackend.Antlr => CreateAntlr(id),
                _ => throw new ArgumentException($"Backend '{backend}' is not valid for '{id}'."),
            };
            registry.Register(lang);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            warn?.WriteLine($"ag4: warning: {ex.Message}; falling back to ANTLR for '{id}'.");
            registry.Register(CreateAntlr(id));
        }

        RebindAliases(registry, id);
    }

    public static void ApplyBackends(
        LanguageRegistry registry,
        IReadOnlyDictionary<string, LanguageBackend>? perLanguage,
        LanguageBackend? globalOverride,
        TextWriter? warn = null
    )
    {
        foreach (var id in SwitchableIds)
        {
            var backend = globalOverride ?? LanguageBackend.TreeSitter;
            if (perLanguage is not null)
            {
                foreach (var (key, value) in perLanguage)
                {
                    if (string.Equals(CanonicalId(key), id, StringComparison.OrdinalIgnoreCase))
                    {
                        backend = value;
                        break;
                    }
                }
            }

            ApplyBackend(registry, id, backend, warn);
        }
    }

    private static void ApplyDefaultBackends(LanguageRegistry registry, LanguageBackend defaultBackend)
    {
        foreach (var id in SwitchableIds)
        {
            ApplyBackend(registry, id, defaultBackend);
        }
    }

    private static void RegisterFixed(LanguageRegistry registry)
    {
        registry.Register(new SqliteLanguage());
        registry.Register(new CSharpLanguage());
        registry.RegisterAlias("cs", "csharp");
        registry.RegisterAlias("c#", "csharp");
        registry.Register(new PlSqlLanguage());
        registry.RegisterAlias("pl/sql", "plsql");
    }

    private static ILanguage CreateTreeSitter(string id) =>
        id.ToLowerInvariant() switch
        {
            "java" => TreeSitterBuiltins.Java(),
            "cpp" => TreeSitterBuiltins.Cpp(),
            "python" => TreeSitterBuiltins.Python(),
            _ => throw new ArgumentException($"No tree-sitter front-end for '{id}'."),
        };

    private static ILanguage CreateAntlr(string id) =>
        id.ToLowerInvariant() switch
        {
            "java" => new JavaLanguage(),
            "cpp" => new CppLanguage(),
            "python" => new PythonLanguage(),
            _ => throw new ArgumentException($"No ANTLR front-end for '{id}'."),
        };

    private static void RebindAliases(LanguageRegistry registry, string id)
    {
        switch (id.ToLowerInvariant())
        {
            case "cpp":
                registry.RegisterAlias("c++", "cpp");
                registry.RegisterAlias("cc", "cpp");
                registry.RegisterAlias("cxx", "cpp");
                break;
            case "python":
                registry.RegisterAlias("py", "python");
                break;
        }
    }

    private static string CanonicalId(string id) =>
        id.ToLowerInvariant() switch
        {
            "c++" or "cc" or "cxx" => "cpp",
            "py" => "python",
            "cs" or "c#" => "csharp",
            "pl/sql" => "plsql",
            var x => x,
        };
}
