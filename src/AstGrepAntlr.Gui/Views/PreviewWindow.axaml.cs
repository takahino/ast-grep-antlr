using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Gui.Services;
using AstGrepAntlr.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace AstGrepAntlr.Gui.Views;

/// <summary>Whole-file view with syntax highlighting, all hits in the file highlighted, and hit navigation.</summary>
public sealed partial class PreviewWindow : Window
{
    private readonly IReadOnlyList<SearchHit> _hits;
    private readonly string _file;
    private TextMate.Installation? _textMate;
    private int _current;

    // Parameterless constructor for the XAML previewer only.
    public PreviewWindow()
        : this(UiStrings.En, null, [], null) { }

    public PreviewWindow(UiStrings t, SearchHit? hit, IReadOnlyList<SearchHit> hitsInFile, string? encoding)
    {
        InitializeComponent();
        _hits = hitsInFile.OrderBy(h => h.Start).ToList();
        _file = hit?.File ?? "";
        Title = $"{t.PreviewTitle} - {Path.GetFileName(_file)}";
        PathText.Text = _file;
        PrevButton.Content = t.PrevHit;
        NextButton.Content = t.NextHit;
        OpenButton.Content = t.OpenInApp;
        if (hit is null)
        {
            return;
        }

        // Read with the search's encoding so hit offsets index the same decoded text.
        var source = SourceFile.Read(_file, encoding);
        Editor.Document = new TextDocument(source.Text);
        Editor.TextArea.TextView.LineTransformers.Add(
            new HighlightTransformer(_hits.Select(h => (h.Start, h.End)).ToList())
        );
        InstallSyntaxHighlighting();
        _current = Math.Max(0, IndexOf(hit));
        Opened += (_, _) => ShowCurrent();
        Closed += (_, _) => _textMate?.Dispose();
        KeyDown += OnKeyDown;
    }

    private void InstallSyntaxHighlighting()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var options = new RegistryOptions(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        var language = options.GetLanguageByExtension(Path.GetExtension(_file));
        if (language is null)
        {
            return;
        }

        _textMate = Editor.InstallTextMate(options);
        _textMate.SetGrammar(options.GetScopeByLanguageId(language.Id));
    }

    private int IndexOf(SearchHit hit)
    {
        for (var i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Start == hit.Start && _hits[i].End == hit.End)
            {
                return i;
            }
        }

        return -1;
    }

    private void ShowCurrent()
    {
        if (_hits.Count == 0)
        {
            return;
        }

        var hit = _hits[_current];
        var length = Editor.Document.TextLength;
        var start = Math.Clamp(hit.Start, 0, length);
        var end = Math.Clamp(hit.End, start, length);
        Editor.Select(start, end - start);
        Editor.TextArea.Caret.Offset = start;
        Editor.ScrollTo(hit.StartLine, hit.StartColumn);
        PositionText.Text = $"{_current + 1} / {_hits.Count}   ({hit.StartLine}:{hit.StartColumn})";
        Editor.Focus();
    }

    private void Move(int delta)
    {
        if (_hits.Count == 0)
        {
            return;
        }

        _current = (_current + delta + _hits.Count) % _hits.Count;
        ShowCurrent();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            Move(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    private void OnPrev(object? sender, RoutedEventArgs e) => Move(-1);

    private void OnNext(object? sender, RoutedEventArgs e) => Move(1);

    private async void OnOpenInApp(object? sender, RoutedEventArgs e)
    {
        if (_file.Length > 0)
        {
            await Launcher.LaunchFileInfoAsync(new FileInfo(_file));
        }
    }
}
