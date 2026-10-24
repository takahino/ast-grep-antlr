using AstGrepAntlr.Pattern;

namespace AstGrepAntlr.Rules;

public sealed class SgRuleDocument
{
    public string? Id { get; set; }
    public string? Language { get; set; }
    public string? Severity { get; set; }
    public string? Message { get; set; }
    public string? Note { get; set; }
    public List<string>? Files { get; set; }
    public List<string>? Ignores { get; set; }
    public RuleNode? Rule { get; set; }
    public Dictionary<string, RuleNode>? Constraints { get; set; }
    public Dictionary<string, RuleNode>? Utils { get; set; }
    public string? Fix { get; set; }
}

public sealed class RuleNode
{
    public string? Pattern { get; set; }
    public PatternObject? PatternObject { get; set; }
    public string? Kind { get; set; }
    public string? Regex { get; set; }
    public RuleNode? Inside { get; set; }
    public RuleNode? Has { get; set; }
    public RuleNode? Precedes { get; set; }
    public RuleNode? Follows { get; set; }
    public string? StopBy { get; set; }
    public List<RuleNode>? All { get; set; }
    public List<RuleNode>? Any { get; set; }
    public RuleNode? Not { get; set; }
    public string? Matches { get; set; }
}

public sealed class PatternObject
{
    public string? Context { get; set; }
    public string? Selector { get; set; }
    public string? Strictness { get; set; }
    public string? Text { get; set; }
    public string? Entry { get; set; }
}

public sealed class SgConfig
{
    public List<string>? RuleDirs { get; set; }
    public List<string>? UtilDirs { get; set; }
    public string? Encoding { get; set; }
    public Dictionary<string, CustomLanguageConfig>? CustomLanguages { get; set; }
}

public sealed class CustomLanguageConfig
{
    public List<string>? Grammar { get; set; }
    public List<string>? Extensions { get; set; }
    public string? StartRule { get; set; }
    public List<string>? PatternRules { get; set; }
    public string? MetaVarPlaceholder { get; set; }
    public List<string>? MetaVarVariants { get; set; }
    public bool IgnoreCase { get; set; }
}
