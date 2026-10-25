using AstGrepAntlr.Language;
using AstGrepAntlr.Rules;

namespace AstGrepAntlr.Languages.Config;

/// <summary>sgconfig.yml helpers shared by the CLI and GUI front ends.</summary>
public static class SgConfigSupport
{
    public static void ApplyAntlrJar(string? antlrJar, string? configPath)
    {
        AntlrJarLocator.ExplicitPath = antlrJar;
        AntlrJarLocator.ConfigDirectory = ConfigBaseDir(configPath);
    }

    public static string? ConfigBaseDir(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(configPath));
    }

    public static string? LoadConfigEncoding(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        return RuleLoader.LoadConfig(File.ReadAllText(configPath)).Encoding;
    }

    public static Dictionary<string, RuleNode>? LoadUtilDirs(SgConfig cfg, string baseDir)
    {
        if (cfg.UtilDirs is null || cfg.UtilDirs.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, RuleNode>(StringComparer.Ordinal);
        foreach (var dir in cfg.UtilDirs)
        {
            var full = Path.IsPathRooted(dir) ? dir : Path.Combine(baseDir, dir);
            foreach (var (doc, _) in RuleLoader.LoadRuleDir(full))
            {
                if (doc.Utils is not null)
                {
                    foreach (var (name, rule) in doc.Utils)
                    {
                        map[name] = rule;
                    }
                }

                // A util file may itself be a named rule referenced via matches: id
                if (!string.IsNullOrEmpty(doc.Id) && doc.Rule is not null)
                {
                    map[doc.Id] = doc.Rule;
                }
            }
        }

        return map.Count == 0 ? null : map;
    }

    public static SgRuleDocument EnsureRuleId(SgRuleDocument doc, string path, TextWriter? stderr)
    {
        if (!string.IsNullOrEmpty(doc.Id))
        {
            return doc;
        }

        var synthetic = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(synthetic))
        {
            synthetic = "unnamed-rule";
        }

        stderr?.WriteLine($"ag4: warning: rule missing id; using '{synthetic}' from {path}");
        doc.Id = synthetic;
        return doc;
    }

    public static string ResolveConfigSearchRoot(IReadOnlyList<string> paths)
    {
        var anchor = paths.Count > 0 ? paths[0] : ".";
        if (File.Exists(anchor))
        {
            return Path.GetDirectoryName(Path.GetFullPath(anchor)) ?? ".";
        }

        return Directory.Exists(anchor) ? Path.GetFullPath(anchor) : Directory.GetCurrentDirectory();
    }
}

public static class SgConfigLocator
{
    public static string? FindUpwards(string? startDirectory = null)
    {
        var dir = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        while (true)
        {
            var candidate = Path.Combine(dir, "sgconfig.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}

public static class CustomLanguageBootstrap
{
    public static void ApplyConfig(
        LanguageRegistry registry,
        string? configPath,
        string? backendCli = null,
        TextWriter? warn = null
    )
    {
        LanguageBackend? global = null;
        if (!string.IsNullOrWhiteSpace(backendCli))
        {
            if (!LanguageBackendNames.TryParse(backendCli, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Unknown backend '{backendCli}'. Use tree-sitter, antlr, or roslyn."
                );
            }

            global = parsed;
        }

        Dictionary<string, LanguageBackend>? perLang = null;
        SgConfig? cfg = null;
        string baseDir = ".";
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            cfg = RuleLoader.LoadConfig(File.ReadAllText(configPath));
            baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
            if (cfg.LanguageBackends is { Count: > 0 })
            {
                perLang = new Dictionary<string, LanguageBackend>(StringComparer.OrdinalIgnoreCase);
                foreach (var (langId, name) in cfg.LanguageBackends)
                {
                    if (LanguageBackendNames.TryParse(name, out var b))
                    {
                        perLang[langId] = b;
                    }
                    else
                    {
                        warn?.WriteLine($"ag4: warning: unknown languageBackend '{name}' for '{langId}'.");
                    }
                }
            }
        }

        if (global is not null || perLang is not null)
        {
            BuiltinLanguages.ApplyBackends(registry, perLang, global, warn);
        }

        if (cfg?.CustomLanguages is null)
        {
            return;
        }

        foreach (var (id, lang) in cfg.CustomLanguages)
        {
            if (lang.Grammar is null || lang.Grammar.Count == 0)
            {
                continue;
            }

            var files = lang.Grammar.Select(g => Path.IsPathRooted(g) ? g : Path.Combine(baseDir, g)).ToList();
            var extensions = lang.Extensions ?? [];
            var startRule = lang.StartRule ?? "program";
            var patternRules = lang.PatternRules ?? [startRule];
            var placeholder = lang.MetaVarPlaceholder ?? "__MV{0}__";
            var ignoreCase = lang.IgnoreCase;
            var variants = lang.MetaVarVariants;
            var langId = id;

            registry.RegisterLazy(
                langId,
                () =>
                {
                    string lexerInterp;
                    string parserInterp;
                    if (files.All(f => f.EndsWith(".interp", StringComparison.OrdinalIgnoreCase)))
                    {
                        lexerInterp = files.First(f => f.Contains("Lexer", StringComparison.OrdinalIgnoreCase));
                        parserInterp = files.First(f => f.Contains("Parser", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        (lexerInterp, parserInterp) = GrammarInterpCache.EnsureInterp(files);
                    }

                    return InterpretedLanguage.FromInterpFiles(
                        langId,
                        extensions,
                        startRule,
                        patternRules,
                        placeholder,
                        ignoreCase,
                        lexerInterp,
                        parserInterp,
                        variants
                    );
                }
            );
        }
    }
}
