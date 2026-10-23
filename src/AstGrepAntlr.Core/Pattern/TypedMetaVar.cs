using Antlr4.Runtime.Atn;
using AstGrepAntlr.Language;

namespace AstGrepAntlr.Pattern;

/// <summary>
/// Support for ${Name:rule} typed metavariables via ATN rule-bypass transitions when available.
/// </summary>
public static class TypedMetaVar
{
    /// <summary>
    /// Rewrite `${A:expression}` into a placeholder and record the expected rule name.
    /// Actual bypass injection is applied when creating a pattern parser ATN clone.
    /// </summary>
    public static string ExtractTyped(string pattern, out IReadOnlyDictionary<string, string> typedRules)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var occ in MetaVar.FindAll(pattern))
        {
            if (occ.TypedRule is not null)
            {
                map[occ.Name] = occ.TypedRule;
            }
        }

        typedRules = map;
        return pattern;
    }

    /// <summary>
    /// Enable rule bypass transitions on a deserialized ATN so identifiers can stand in for arbitrary rules.
    /// Callers that own ATN deserialization should set GenerateRuleBypassTransitions = true.
    /// </summary>
    public static ATNDeserializationOptions BypassOptions()
    {
        var opts = new ATNDeserializationOptions();
        // Property may be GenerateRuleBypassTransitions on ATNDeserializationOptions.
        try
        {
            var prop = typeof(ATNDeserializationOptions).GetProperty("GenerateRuleBypassTransitions");
            prop?.SetValue(opts, true);
        }
        catch
        {
            // Older runtimes: typed metavars degrade to normal placeholders.
        }

        return opts;
    }

    public static bool ConstraintSatisfied(ILanguage language, string typedRule, string capturedText)
    {
        if (string.IsNullOrEmpty(typedRule))
        {
            return true;
        }

        try
        {
            var parser = new Parsing.TwoStageParser();
            var result = parser.Parse(
                language,
                capturedText,
                new LanguageParseOptions { EntryRule = typedRule, PreferSll = true }
            );
            return result.Diagnostics.Count == 0 || result.Tree.NodeCount > 0;
        }
        catch
        {
            return false;
        }
    }
}
