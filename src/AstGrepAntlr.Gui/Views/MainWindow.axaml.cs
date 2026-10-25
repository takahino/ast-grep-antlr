using System.ComponentModel;
using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Gui.Services;
using AstGrepAntlr.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;

namespace AstGrepAntlr.Gui.Views;

public sealed partial class MainWindow : Window
{
    private const int TableTabIndex = 1;
    private MainViewModel? _vm;
    private HighlightTransformer? _codeHighlighter;

    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so Ctrl+F wins over the TextBox / editor default handling.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Closing += (_, _) => _vm?.SaveSettings(Width, Height);
    }

    public void ApplySettings(AppSettings settings)
    {
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
    }

    private MainViewModel Vm => _vm ?? throw new InvalidOperationException("DataContext is not set.");

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as MainViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyColumnHeaders();
        UpdateCodeView();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CodeView):
                UpdateCodeView();
                break;
            case nameof(MainViewModel.T):
                ApplyColumnHeaders();
                break;
        }
    }

    private void ApplyColumnHeaders()
    {
        // DataGrid columns are not in the visual tree, so their headers cannot bind to the view model.
        var t = Vm.T;
        string[] headers = [t.ColFile, t.ColLine, t.ColColumn, t.ColText, t.ColMeta, t.ColRule, t.ColMessage];
        for (var i = 0; i < ResultGrid.Columns.Count && i < headers.Length; i++)
        {
            ResultGrid.Columns[i].Header = headers[i];
        }
    }

    private void UpdateCodeView()
    {
        var doc = Vm.CodeView;
        CodeEditor.Document = new TextDocument(doc.Text);
        var transformers = CodeEditor.TextArea.TextView.LineTransformers;
        if (_codeHighlighter is not null)
        {
            transformers.Remove(_codeHighlighter);
        }

        _codeHighlighter = new HighlightTransformer(doc.MatchRanges, doc.HeaderLines);
        transformers.Add(_codeHighlighter);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (ResultTabs.SelectedIndex == TableTabIndex)
        {
            TableFindBar.IsVisible = true;
            TableFindBox.Focus();
            TableFindBox.SelectAll();
            e.Handled = true;
        }
        else if (CodeEditor.IsKeyboardFocusWithin)
        {
            // AvaloniaEdit's built-in search panel (highlight all, next / previous, case option).
            CodeEditor.SearchPanel.Open();
            e.Handled = true;
        }
    }

    // ---- Search conditions ----

    private async void OnBrowseRoot(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false, Title = Vm.T.Root }
        );
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            Vm.Root = path;
        }
    }

    private async void OnBrowseRootFile(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(Vm.T.Root, null);
        if (path is not null)
        {
            Vm.Root = path;
        }
    }

    private async void OnBrowseRule(object? sender, RoutedEventArgs e)
    {
        var yaml = new FilePickerFileType("YAML") { Patterns = ["*.yml", "*.yaml"] };
        var path = await PickFileAsync(Vm.T.RulePath, yaml);
        if (path is not null)
        {
            Vm.RulePath = path;
        }
    }

    private async Task<string?> PickFileAsync(string title, FilePickerFileType? type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = title,
                FileTypeFilter = type is null ? null : [type, FilePickerFileTypes.All],
            }
        );
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void OnShowHistory(object? sender, RoutedEventArgs e) =>
        ShowPatternMenu(HistoryButton, Vm.History.ToList(), Vm.T.NoHistory);

    private void OnShowPresets(object? sender, RoutedEventArgs e) => ShowPatternMenu(PresetButton, Vm.Presets, "-");

    private void ShowPatternMenu(Control anchor, IReadOnlyList<string> patterns, string emptyText)
    {
        var flyout = new MenuFlyout();
        if (patterns.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = emptyText, IsEnabled = false });
        }

        foreach (var pattern in patterns)
        {
            var oneLine = pattern.ReplaceLineEndings(" ⏎ ");
            var item = new MenuItem { Header = oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine };
            item.Click += (_, _) => Vm.UsePattern(pattern);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(anchor);
    }

    private void OnShowHelp(object? sender, RoutedEventArgs e) => new HelpWindow(Vm.T).Show(this);

    // ---- Actions ----

    private async void OnRewritePreview(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm.LastRequest?.Mode is SearchMode.Text or SearchMode.Regex)
        {
            vm.Status = vm.T.RewriteAstOnly;
            return;
        }

        try
        {
            var hits = vm.Hits;
            var template = vm.Rewrite;
            var encoding = vm.LastSearchEncoding();
            var previews = await Task.Run(() => RewriteService.Preview(hits, template, encoding));
            var rewriteVm = new RewriteViewModel(vm.T, previews);
            rewriteVm.Applied += (_, _) => _ = vm.SearchAsync(addToHistory: false);
            await new RewriteWindow { DataContext = rewriteVm }.ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.Status = vm.T.StatusError + MainViewModel.Describe(ex);
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        var file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = vm.T.Export,
                SuggestedFileName = "ag4-results.txt",
                DefaultExtension = "txt",
                FileTypeChoices =
                [
                    new FilePickerFileType("Text") { Patterns = ["*.txt"] },
                    new FilePickerFileType("JSON") { Patterns = ["*.json"] },
                    new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
                    new FilePickerFileType("HTML") { Patterns = ["*.html"] },
                    new FilePickerFileType("CSV") { Patterns = ["*.csv"] },
                ],
            }
        );
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            var format = ExportService.FormatFromExtension(path);
            ExportService.Write(path, format, vm.LastRequest ?? vm.Snapshot(), vm.Hits);
            vm.Status = vm.T.ExportDone + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.Status = vm.T.StatusError + ex.Message;
        }
    }

    private async void OnCopyResults(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(Vm.ResultsAsText());
        Vm.Status = Vm.T.CopyDone;
    }

    // ---- Result navigation ----

    private SearchHit? CodeHitAtCaret() =>
        Vm.CodeView.LineHits.TryGetValue(CodeEditor.TextArea.Caret.Line, out var hit) ? hit : null;

    private SearchHit? SelectedFileHit() =>
        Vm.SelectedFile?.File is { } file && Vm.HitsInFile(file) is { Count: > 0 } hits ? hits[0] : null;

    private void OnCodeDoubleTapped(object? sender, TappedEventArgs e) => OpenPreview(CodeHitAtCaret());

    private void OnCodePreview(object? sender, RoutedEventArgs e) => OpenPreview(CodeHitAtCaret());

    private void OnCodeOpenInApp(object? sender, RoutedEventArgs e) => OpenInApp(CodeHitAtCaret());

    private void OnCodeCopyPath(object? sender, RoutedEventArgs e) => CopyPath(CodeHitAtCaret());

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e) =>
        OpenPreview(ResultGrid.SelectedItem as SearchHit);

    private void OnGridPreview(object? sender, RoutedEventArgs e) => OpenPreview(ResultGrid.SelectedItem as SearchHit);

    private void OnGridOpenInApp(object? sender, RoutedEventArgs e) => OpenInApp(ResultGrid.SelectedItem as SearchHit);

    private void OnGridCopyPath(object? sender, RoutedEventArgs e) => CopyPath(ResultGrid.SelectedItem as SearchHit);

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e) => OpenPreview(SelectedFileHit());

    private void OnFilePreview(object? sender, RoutedEventArgs e) => OpenPreview(SelectedFileHit());

    private void OnFileOpenInApp(object? sender, RoutedEventArgs e) => OpenInApp(SelectedFileHit());

    private void OnFileCopyPath(object? sender, RoutedEventArgs e) => CopyPath(SelectedFileHit());

    private void OpenPreview(SearchHit? hit)
    {
        if (hit is null)
        {
            return;
        }

        try
        {
            new PreviewWindow(Vm.T, hit, Vm.HitsInFile(hit.File), Vm.LastSearchEncoding()).Show(this);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            Vm.Status = Vm.T.StatusError + ex.Message;
        }
    }

    private async void OpenInApp(SearchHit? hit)
    {
        if (hit is not null && !await Launcher.LaunchFileInfoAsync(new FileInfo(hit.File)))
        {
            Vm.Status = Vm.T.StatusError + hit.File;
        }
    }

    private async void CopyPath(SearchHit? hit)
    {
        if (hit is not null && Clipboard is not null)
        {
            await Clipboard.SetTextAsync(hit.File);
        }
    }

    // ---- Table find bar ----

    private void OnTableFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInTable(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TableFindBar.IsVisible = false;
            e.Handled = true;
        }
    }

    private void OnTableFindNext(object? sender, RoutedEventArgs e) => FindInTable(1);

    private void OnTableFindPrev(object? sender, RoutedEventArgs e) => FindInTable(-1);

    private void OnTableFindClose(object? sender, RoutedEventArgs e) => TableFindBar.IsVisible = false;

    private void FindInTable(int direction)
    {
        var query = TableFindBox.Text;
        var rows = Vm.VisibleHits;
        if (string.IsNullOrEmpty(query) || rows.Count == 0)
        {
            return;
        }

        var comparison =
            TableFindCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var current = ResultGrid.SelectedItem is SearchHit selected ? IndexOf(rows, selected) : -1;
        for (var step = 1; step <= rows.Count; step++)
        {
            var index = ((current + (direction * step)) % rows.Count + rows.Count) % rows.Count;
            var row = rows[index];
            if (
                row.DisplayPath.Contains(query, comparison)
                || row.Text.Contains(query, comparison)
                || row.MetaSummary.Contains(query, comparison)
                || (row.Message?.Contains(query, comparison) ?? false)
            )
            {
                ResultGrid.SelectedItem = row;
                ResultGrid.ScrollIntoView(row, null);
                return;
            }
        }
    }

    private static int IndexOf(IReadOnlyList<SearchHit> rows, SearchHit hit)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], hit))
            {
                return i;
            }
        }

        return -1;
    }
}
