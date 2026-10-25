using System.ComponentModel;
using AstGrepAntlr.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace AstGrepAntlr.Gui.Views;

public sealed partial class RewriteWindow : Window
{
    private RewriteViewModel? _vm;
    private TextMate.Installation? _textMate;

    public RewriteWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => InstallDiffHighlighting();
        Closed += (_, _) => _textMate?.Dispose();
    }

    private void InstallDiffHighlighting()
    {
        var options = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus
        );
        _textMate = DiffEditor.InstallTextMate(options);
        _textMate.SetGrammar(options.GetScopeByExtension(".diff"));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as RewriteViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            ShowDiff();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RewriteViewModel.DiffText))
        {
            ShowDiff();
        }
    }

    // AvaloniaEdit's Text is not a bindable styled property, so the diff is pushed from here.
    private void ShowDiff() => DiffEditor.Document = new TextDocument(_vm?.DiffText ?? "");

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
