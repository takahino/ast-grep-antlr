namespace AstGrepAntlr.Languages;

/// <summary>Parser backend for languages that support more than one.</summary>
public enum LanguageBackend
{
    /// <summary>Prefer tree-sitter when available (default for Java / C++ / Python).</summary>
    TreeSitter,

    /// <summary>ANTLR4 grammars-v4 / interpreted DSL.</summary>
    Antlr,

    /// <summary>Roslyn (C# only).</summary>
    Roslyn,
}

public static class LanguageBackendNames
{
    public static bool TryParse(string? text, out LanguageBackend backend)
    {
        backend = LanguageBackend.TreeSitter;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "tree-sitter":
            case "treesitter":
            case "ts":
                backend = LanguageBackend.TreeSitter;
                return true;
            case "antlr":
            case "antlr4":
                backend = LanguageBackend.Antlr;
                return true;
            case "roslyn":
                backend = LanguageBackend.Roslyn;
                return true;
            default:
                return false;
        }
    }

    public static string ToCli(LanguageBackend backend) =>
        backend switch
        {
            LanguageBackend.TreeSitter => "tree-sitter",
            LanguageBackend.Antlr => "antlr",
            LanguageBackend.Roslyn => "roslyn",
            _ => backend.ToString().ToLowerInvariant(),
        };
}
