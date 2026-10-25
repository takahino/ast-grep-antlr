using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.TreeSitter;

namespace AstGrepAntlr.Languages;

public static class BuiltinLanguages
{
    /// <summary>Languages that can switch parser backends.</summary>
    public static readonly IReadOnlyList<string> SwitchableIds = ["java", "cpp", "python", "csharp"];

    public static LanguageRegistry CreateRegistry(LanguageBackend defaultBackend = LanguageBackend.TreeSitter)
    {
        var registry = new LanguageRegistry();
        RegisterFixed(registry);
        ApplyDefaultBackends(registry, defaultBackend);
        return registry;
    }

    /// <summary>
    /// Re-register <paramref name="languageId"/> with the chosen backend.
    /// Aliases (<c>c++</c>, <c>py</c>, <c>cs</c>, …) are refreshed so they point at the new instance.
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

        if (!IsBackendAllowed(id, backend))
        {
            warn?.WriteLine(
                $"ag4: warning: backend '{LanguageBackendNames.ToCli(backend)}' is not valid for '{id}'; using default."
            );
            backend = DefaultBackendFor(id);
        }

        var chosen = backend;
        registry.RegisterLazy(
            id,
            () =>
            {
                var effective = chosen;
                if (effective == LanguageBackend.TreeSitter && !TreeSitterLanguage.NativesAvailable)
                {
                    var fallback = FallbackBackend(id);
                    warn?.WriteLine(
                        $"ag4: warning: tree-sitter natives unavailable; falling back to {LanguageBackendNames.ToCli(fallback)} for '{id}'."
                    );
                    effective = fallback;
                }

                try
                {
                    return CreateLanguage(id, effective);
                }
                catch (Exception ex)
                    when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    var fallback = FallbackBackend(id);
                    warn?.WriteLine(
                        $"ag4: warning: {ex.Message}; falling back to {LanguageBackendNames.ToCli(fallback)} for '{id}'."
                    );
                    return CreateLanguage(id, fallback);
                }
            }
        );

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
            var backend = ResolveBackend(id, perLanguage, globalOverride, warn);
            ApplyBackend(registry, id, backend, warn);
        }
    }

    private static LanguageBackend ResolveBackend(
        string id,
        IReadOnlyDictionary<string, LanguageBackend>? perLanguage,
        LanguageBackend? globalOverride,
        TextWriter? warn
    )
    {
        if (perLanguage is not null)
        {
            foreach (var (key, value) in perLanguage)
            {
                if (!string.Equals(CanonicalId(key), id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsBackendAllowed(id, value))
                {
                    warn?.WriteLine(
                        $"ag4: warning: languageBackends '{id}: {LanguageBackendNames.ToCli(value)}' is not supported; using default."
                    );
                    return DefaultBackendFor(id);
                }

                return value;
            }
        }

        if (globalOverride is { } g && IsBackendAllowed(id, g))
        {
            return g;
        }

        return DefaultBackendFor(id);
    }

    /// <summary>Whether <paramref name="backend"/> is a valid choice for <paramref name="languageId"/>.</summary>
    public static bool IsBackendAllowed(string languageId, LanguageBackend backend)
    {
        var id = CanonicalId(languageId);
        return id.ToLowerInvariant() switch
        {
            "java" or "cpp" or "python" => backend is LanguageBackend.TreeSitter or LanguageBackend.Antlr,
            "csharp" => backend is LanguageBackend.TreeSitter or LanguageBackend.Roslyn,
            _ => false,
        };
    }

    private static LanguageBackend DefaultBackendFor(string id) => LanguageBackend.TreeSitter;

    private static LanguageBackend FallbackBackend(string id) =>
        id.ToLowerInvariant() switch
        {
            "csharp" => LanguageBackend.Roslyn,
            _ => LanguageBackend.Antlr,
        };

    private static void ApplyDefaultBackends(LanguageRegistry registry, LanguageBackend defaultBackend)
    {
        foreach (var id in SwitchableIds)
        {
            var backend = IsBackendAllowed(id, defaultBackend) ? defaultBackend : DefaultBackendFor(id);
            ApplyBackend(registry, id, backend);
        }
    }

    /// <summary>tree-sitter-only languages (no backend switching): id, factory, aliases.</summary>
    private static readonly (string Id, Func<ILanguage> Factory, string[] Aliases)[] s_treeSitterOnly =
    [
        ("javascript", TreeSitterBuiltins.JavaScript, ["js", "jsx"]),
        ("typescript", TreeSitterBuiltins.TypeScript, ["ts"]),
        ("tsx", TreeSitterBuiltins.Tsx, []),
        ("go", TreeSitterBuiltins.Go, ["golang"]),
        ("rust", TreeSitterBuiltins.Rust, ["rs"]),
        ("c", TreeSitterBuiltins.C, []),
        ("ruby", TreeSitterBuiltins.Ruby, ["rb"]),
        ("php", TreeSitterBuiltins.Php, []),
        ("bash", TreeSitterBuiltins.Bash, ["sh"]),
        ("html", TreeSitterBuiltins.Html, []),
        ("css", TreeSitterBuiltins.Css, []),
        ("json", TreeSitterBuiltins.Json, []),
        ("scala", TreeSitterBuiltins.Scala, []),
        ("haskell", TreeSitterBuiltins.Haskell, ["hs"]),
        ("julia", TreeSitterBuiltins.Julia, ["jl"]),
        ("ocaml", TreeSitterBuiltins.OCaml, ["ml"]),
        ("toml", TreeSitterBuiltins.Toml, []),
        ("verilog", TreeSitterBuiltins.Verilog, ["v"]),
    ];

    private static void RegisterFixed(LanguageRegistry registry)
    {
        registry.RegisterLazy("sqlite", static () => new SqliteLanguage());
        registry.RegisterLazy("plsql", static () => new PlSqlLanguage());
        registry.RegisterAlias("pl/sql", "plsql");

        foreach (var (id, factory, aliases) in s_treeSitterOnly)
        {
            registry.RegisterLazy(id, () => CreateTreeSitterOnly(id, factory));
            foreach (var alias in aliases)
            {
                registry.RegisterAlias(alias, id);
            }
        }
    }

    private static ILanguage CreateTreeSitterOnly(string id, Func<ILanguage> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // No ANTLR fallback exists for these; some grammars (e.g. toml, ocaml) ship only for some RIDs.
            // (tree-sitter-swift in TreeSitter.DotNet 1.3.0 is ABI 10, below the core's minimum of 13, so it is not offered.)
            throw new PlatformNotSupportedException(
                $"Language '{id}' is not available on this platform ({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}): {ex.Message}",
                ex
            );
        }
    }

    private static ILanguage CreateLanguage(string id, LanguageBackend backend) =>
        backend switch
        {
            LanguageBackend.TreeSitter => CreateTreeSitter(id),
            LanguageBackend.Antlr => CreateAntlr(id),
            LanguageBackend.Roslyn => CreateRoslyn(id),
            _ => throw new ArgumentException($"Backend '{backend}' is not valid for '{id}'."),
        };

    private static ILanguage CreateTreeSitter(string id) =>
        id.ToLowerInvariant() switch
        {
            "java" => TreeSitterBuiltins.Java(),
            "cpp" => TreeSitterBuiltins.Cpp(),
            "python" => TreeSitterBuiltins.Python(),
            "csharp" => TreeSitterBuiltins.CSharp(),
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

    private static ILanguage CreateRoslyn(string id) =>
        id.ToLowerInvariant() switch
        {
            "csharp" => new CSharpLanguage(),
            _ => throw new ArgumentException($"No Roslyn front-end for '{id}'."),
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
            case "csharp":
                registry.RegisterAlias("cs", "csharp");
                registry.RegisterAlias("c#", "csharp");
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
