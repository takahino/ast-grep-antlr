using AstGrepAntlr.Language;

namespace AstGrepAntlr.Languages;

public static class BuiltinLanguages
{
    public static LanguageRegistry CreateRegistry()
    {
        var registry = new LanguageRegistry();
        registry.Register(new JavaLanguage());
        registry.Register(new SqliteLanguage());
        registry.Register(new CppLanguage());
        registry.RegisterAlias("c++", "cpp");
        registry.RegisterAlias("cc", "cpp");
        registry.RegisterAlias("cxx", "cpp");
        // Additional languages (js/ts/csharp/plsql/postgresql/mysql) can be added as
        // generated parsers or via sgconfig.yml customLanguages (.g4 / .interp).
        return registry;
    }
}
