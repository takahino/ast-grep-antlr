using AstGrepAntlr.Gui.Services;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace AstGrepAntlr.Gui.Views;

/// <summary>Shows the pattern / rule docs shipped with the MCP server (embedded at build time).</summary>
public sealed partial class HelpWindow : Window
{
    private readonly List<TextMate.Installation> _installations = [];

    // Parameterless constructor for the XAML previewer only.
    public HelpWindow()
        : this(UiStrings.En) { }

    public HelpWindow(UiStrings t)
    {
        InitializeComponent();
        Title = t.HelpTitle;
        PatternTab.Header = t.HelpPattern;
        RuleTab.Header = t.HelpRule;
        PatternEditor.Document = new TextDocument(ReadResource("AstGrepAntlr.Gui.pattern-syntax.md"));
        RuleEditor.Document = new TextDocument(ReadResource("AstGrepAntlr.Gui.rule-yaml.md"));
        Opened += (_, _) =>
        {
            Highlight(PatternEditor);
            Highlight(RuleEditor);
        };
        Closed += (_, _) => _installations.ForEach(i => i.Dispose());
    }

    private void Highlight(TextEditor editor)
    {
        var options = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus
        );
        var installation = editor.InstallTextMate(options);
        installation.SetGrammar(options.GetScopeByExtension(".md"));
        _installations.Add(installation);
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(HelpWindow).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return $"(missing embedded resource: {name})";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
