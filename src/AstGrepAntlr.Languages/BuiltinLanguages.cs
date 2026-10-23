using AstGrepAntlr.Language;

namespace AstGrepAntlr.Languages;

public static class BuiltinLanguages
{
    public static LanguageRegistry CreateRegistry()
    {
        var registry = new LanguageRegistry();
        registry.Register(new JavaLanguage());
        registry.Register(new SqliteLanguage());
        // Additional languages (js/ts/cpp/csharp/plsql/postgresql/mysql) can be added as
        // generated parsers or via sgconfig.yml customLanguages (.g4 / .interp).
        return registry;
    }
}
