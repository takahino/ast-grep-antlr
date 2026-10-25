namespace AstGrepAntlr.Gui.Models;

public enum SearchMode
{
    Ast,
    Rule,
    Text,
    Regex,
}

public enum RuleSourceKind
{
    /// <summary>sgconfig.yml (explicit path, or auto-detected upwards from the search root).</summary>
    Config,

    /// <summary>A single rule YAML file.</summary>
    File,

    /// <summary>Rule YAML typed into the pattern box; multiple documents separated by <c>---</c>.</summary>
    Inline,
}

/// <summary>Immutable snapshot of every search condition; also embedded in exports.</summary>
public sealed record SearchRequest
{
    public const string AutoLanguage = "auto";
    public const string AutoEncoding = "auto";
    public const string DefaultBackend = "default";

    public SearchMode Mode { get; init; } = SearchMode.Ast;
    public string Root { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string Language { get; init; } = AutoLanguage;
    public string Strictness { get; init; } = "smart";
    public string Backend { get; init; } = DefaultBackend;
    public string Rewrite { get; init; } = "";
    public RuleSourceKind RuleSource { get; init; } = RuleSourceKind.Config;
    public string RulePath { get; init; } = "";
    public string RuleIdFilter { get; init; } = "";
    public bool CaseSensitive { get; init; } = true;
    public bool WholeWord { get; init; }

    /// <summary>File globs separated by <c>;</c>, <c>,</c> or newlines (e.g. <c>*.java;src/**</c>).</summary>
    public string Globs { get; init; } = "";

    /// <summary>Directory names to skip, separated like <see cref="Globs"/>.</summary>
    public string ExcludeDirs { get; init; } = "";
    public string Encoding { get; init; } = AutoEncoding;
    public int ContextLines { get; init; } = 2;
    public int MaxResults { get; init; } = 100_000;
    public bool NoIgnore { get; init; }
    public bool IncludeHidden { get; init; }
}
