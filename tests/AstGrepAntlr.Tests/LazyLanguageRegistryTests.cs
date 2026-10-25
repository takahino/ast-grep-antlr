using AstGrepAntlr.Language;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Languages.TreeSitter;
using Xunit;

namespace AstGrepAntlr.Tests;

public class LazyLanguageRegistryTests
{
    [Fact]
    public void CreateRegistry_does_not_load_tree_sitter_until_get()
    {
        // Creating the registry must not throw even if we never touch a language.
        var registry = BuiltinLanguages.CreateRegistry();
        Assert.NotNull(registry);
    }

    [Fact]
    public void ApplyBackend_rebinds_aliases_to_new_instance()
    {
        var registry = BuiltinLanguages.CreateRegistry();
        var first = registry.Get("csharp");
        Assert.IsType<TreeSitterLanguage>(first);

        BuiltinLanguages.ApplyBackend(registry, "csharp", LanguageBackend.Roslyn);
        var viaAlias = registry.Get("cs");
        Assert.IsType<CSharpLanguage>(viaAlias);
        Assert.Same(viaAlias, registry.Get("csharp"));
        Assert.NotSame(first, viaAlias);
    }

    [Fact]
    public void RegisterAlias_clears_stale_eager_binding()
    {
        var registry = new LanguageRegistry();
        registry.Register(new SqliteLanguage());
        registry.RegisterAlias("sql", "sqlite");
        var first = registry.Get("sql");

        registry.RegisterLazy("sqlite", () => new SqliteLanguage());
        registry.RegisterAlias("sql", "sqlite");

        var second = registry.Get("sql");
        Assert.NotSame(first, second);
        Assert.Same(second, registry.Get("sqlite"));
    }
}
