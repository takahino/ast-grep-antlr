using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AstGrepAntlr.Gui.Infrastructure;
using AstGrepAntlr.Gui.Models;
using AstGrepAntlr.Gui.Services;
using Avalonia.Threading;

namespace AstGrepAntlr.Gui.ViewModels;

/// <summary>Entry in the file list; <see cref="File"/> is null for the "(all)" row.</summary>
public sealed record FileGroup(string? File, string DisplayPath, int Count);

/// <summary>ComboBox entry whose text follows the UI language.</summary>
public sealed class LabelItem : ViewModelBase
{
    private string _label = "";

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }
}

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private const int CodeViewMaxHits = 5000;
    private static readonly TimeSpan IncrementalDelay = TimeSpan.FromMilliseconds(500);

    private static readonly string[] AstPresets =
    [
        "$FUNC($$$ARGS)",
        "$OBJ.$METHOD($$$ARGS)",
        "new $TYPE($$$ARGS)",
        "if ($COND) { $$$BODY }",
        "$A = $A",
        "catch ($E) {}",
    ];

    private static readonly string[] RulePresets =
    [
        "id: my-rule\nlanguage: java\nseverity: warning\nmessage: Use Objects.equals\nrule:\n  pattern: $A.equals($B)\nfix: Objects.equals($A, $B)",
        "id: call-inside-loop\nlanguage: java\nrule:\n  pattern: $FUNC($$$ARGS)\n  inside:\n    kind: for_statement\n    stopBy: end",
    ];

    private static readonly string[] RegexPresets = [@"\bTODO\b.*", @"\b\w+Exception\b", @"""[^""]*"""];

    private readonly SettingsStore _store;
    private readonly SearchService _search;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _debounceCts;
    private Dictionary<string, List<SearchHit>> _hitsByFile = [];

    private UiStrings _t;
    private string _root = "";
    private int _modeIndex;
    private string _language = SearchRequest.AutoLanguage;
    private string _strictness = "smart";
    private string _backend = SearchRequest.DefaultBackend;
    private string _pattern = "";
    private string _rewrite = "";
    private int _ruleSourceIndex;
    private string _rulePath = "";
    private string _ruleIdFilter = "";
    private bool _caseSensitive = true;
    private bool _wholeWord;
    private string _globs = "";
    private string _excludeDirs = "";
    private string _encoding = SearchRequest.AutoEncoding;
    private decimal? _contextLines = 2;
    private decimal? _maxResults = 100_000;
    private bool _noIgnore;
    private bool _includeHidden;
    private bool _incremental;
    private bool _isSearching;
    private string _status;
    private IReadOnlyList<SearchHit> _hits = [];
    private IReadOnlyList<SearchHit> _visibleHits = [];
    private IReadOnlyList<FileGroup> _files = [];
    private FileGroup? _selectedFile;
    private CodeViewDocument _codeView = CodeViewDocument.Empty;
    private IReadOnlyList<string> _warnings = [];

    public MainViewModel(SettingsStore store, SearchService search)
    {
        _store = store;
        _search = search;
        var (settings, error) = store.Load();
        Settings = settings;
        _t = UiStrings.For(settings.UiLanguage);
        LoadRequest(settings.Last);
        _incremental = settings.Incremental;
        History = new ObservableCollection<string>(settings.History.Take(AppSettings.MaxHistory));
        Languages = [SearchRequest.AutoLanguage];
        if (!Languages.Contains(_language))
        {
            Languages.Add(_language);
        }

        RebuildLabels();
        _status = error is null ? _t.StatusReady : _t.SettingsLoadFailed + error;

        SearchCommand = new RelayCommand(() => _ = SearchAsync(), () => !IsSearching);
        CancelCommand = new RelayCommand(CancelSearch, () => IsSearching);
        ToggleUiLanguageCommand = new RelayCommand(ToggleUiLanguage);
        LoadLanguagesInBackground();
    }

    public AppSettings Settings { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleUiLanguageCommand { get; }

    public ObservableCollection<string> History { get; }
    public ObservableCollection<string> Languages { get; }
    public IReadOnlyList<string> StrictnessOptions { get; } = ["cst", "smart", "ast", "relaxed"];
    public IReadOnlyList<string> BackendOptions { get; } =
    [SearchRequest.DefaultBackend, "tree-sitter", "antlr", "roslyn"];
    public IReadOnlyList<string> EncodingOptions { get; } =
    [SearchRequest.AutoEncoding, "utf-8", "utf-8-bom", "shift_jis", "euc-jp", "utf-16", "utf-16be"];

    /// <summary>The request that produced <see cref="Hits"/> (exports and rewrite use it, not the live form).</summary>
    public SearchRequest? LastRequest { get; private set; }

    public UiStrings T
    {
        get => _t;
        private set
        {
            if (SetField(ref _t, value))
            {
                RebuildLabels();
                OnPropertyChanged(nameof(PatternHint));
                OnPropertyChanged(nameof(RulePathHint));
            }
        }
    }

    public IReadOnlyList<LabelItem> ModeLabels { get; } = [new(), new(), new(), new()];

    public IReadOnlyList<LabelItem> RuleSourceLabels { get; } = [new(), new(), new()];

    public string Root
    {
        get => _root;
        set => SetField(ref _root, value);
    }

    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            // A ComboBox can transiently report -1 (no selection); never treat that as a mode.
            if (value < 0 || !SetField(ref _modeIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsAstMode));
            OnPropertyChanged(nameof(IsRuleMode));
            OnPropertyChanged(nameof(IsTextMode));
            OnPropertyChanged(nameof(IsPlainTextMode));
            OnPropertyChanged(nameof(IsStructuralMode));
            OnPropertyChanged(nameof(PatternHint));
            OnPropertyChanged(nameof(Presets));
        }
    }

    public SearchMode Mode => (SearchMode)_modeIndex;
    public bool IsAstMode => Mode == SearchMode.Ast;
    public bool IsRuleMode => Mode == SearchMode.Rule;
    public bool IsTextMode => Mode is SearchMode.Text or SearchMode.Regex;
    public bool IsPlainTextMode => Mode == SearchMode.Text;
    public bool IsStructuralMode => Mode is SearchMode.Ast or SearchMode.Rule;

    public string PatternHint =>
        Mode switch
        {
            SearchMode.Rule => T.PatternHintRule,
            SearchMode.Text => T.PatternHintText,
            SearchMode.Regex => T.PatternHintRegex,
            _ => T.PatternHintAst,
        };

    public IReadOnlyList<string> Presets =>
        Mode switch
        {
            SearchMode.Ast => AstPresets,
            SearchMode.Rule => RulePresets,
            SearchMode.Regex => RegexPresets,
            _ => [],
        };

    public string Language
    {
        get => _language;
        set
        {
            if (value is not null)
            {
                SetField(ref _language, value);
            }
        }
    }

    public string Strictness
    {
        get => _strictness;
        set
        {
            if (value is not null)
            {
                SetField(ref _strictness, value);
            }
        }
    }

    public string Backend
    {
        get => _backend;
        set
        {
            if (value is not null)
            {
                SetField(ref _backend, value);
            }
        }
    }

    public string Pattern
    {
        get => _pattern;
        set
        {
            if (SetField(ref _pattern, value ?? "") && Incremental)
            {
                ScheduleIncrementalSearch();
            }
        }
    }

    public string Rewrite
    {
        get => _rewrite;
        set => SetField(ref _rewrite, value ?? "");
    }

    public int RuleSourceIndex
    {
        get => _ruleSourceIndex;
        set
        {
            if (value >= 0 && SetField(ref _ruleSourceIndex, value))
            {
                OnPropertyChanged(nameof(RulePathHint));
                OnPropertyChanged(nameof(IsRulePathVisible));
            }
        }
    }

    public bool IsRulePathVisible => (RuleSourceKind)_ruleSourceIndex != RuleSourceKind.Inline;

    public string RulePathHint =>
        (RuleSourceKind)_ruleSourceIndex == RuleSourceKind.File ? T.RulePathFileHint : T.RulePathConfigHint;

    public string RulePath
    {
        get => _rulePath;
        set => SetField(ref _rulePath, value ?? "");
    }

    public string RuleIdFilter
    {
        get => _ruleIdFilter;
        set => SetField(ref _ruleIdFilter, value ?? "");
    }

    public bool CaseSensitive
    {
        get => _caseSensitive;
        set => SetField(ref _caseSensitive, value);
    }

    public bool WholeWord
    {
        get => _wholeWord;
        set => SetField(ref _wholeWord, value);
    }

    public string Globs
    {
        get => _globs;
        set => SetField(ref _globs, value ?? "");
    }

    public string ExcludeDirs
    {
        get => _excludeDirs;
        set => SetField(ref _excludeDirs, value ?? "");
    }

    public string Encoding
    {
        get => _encoding;
        set
        {
            if (value is not null)
            {
                SetField(ref _encoding, value);
            }
        }
    }

    // NumericUpDown binds decimal?; converted to int in Snapshot().
    public decimal? ContextLines
    {
        get => _contextLines;
        set => SetField(ref _contextLines, value);
    }

    public decimal? MaxResults
    {
        get => _maxResults;
        set => SetField(ref _maxResults, value);
    }

    public bool NoIgnore
    {
        get => _noIgnore;
        set => SetField(ref _noIgnore, value);
    }

    public bool IncludeHidden
    {
        get => _includeHidden;
        set => SetField(ref _includeHidden, value);
    }

    public bool Incremental
    {
        get => _incremental;
        set => SetField(ref _incremental, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetField(ref _isSearching, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public IReadOnlyList<SearchHit> Hits
    {
        get => _hits;
        private set
        {
            if (SetField(ref _hits, value))
            {
                OnPropertyChanged(nameof(HasHits));
            }
        }
    }

    public bool HasHits => _hits.Count > 0;

    public IReadOnlyList<SearchHit> VisibleHits
    {
        get => _visibleHits;
        private set => SetField(ref _visibleHits, value);
    }

    public IReadOnlyList<FileGroup> Files
    {
        get => _files;
        private set => SetField(ref _files, value);
    }

    public FileGroup? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
            {
                RefreshVisibleHits();
            }
        }
    }

    public CodeViewDocument CodeView
    {
        get => _codeView;
        private set => SetField(ref _codeView, value);
    }

    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        private set
        {
            if (SetField(ref _warnings, value))
            {
                OnPropertyChanged(nameof(WarningSummary));
                OnPropertyChanged(nameof(WarningDetails));
            }
        }
    }

    public string WarningSummary =>
        _warnings.Count == 0 ? "" : string.Format(CultureInfo.CurrentCulture, T.Warnings, _warnings.Count);

    public string WarningDetails => string.Join(Environment.NewLine, _warnings.Take(50));

    /// <summary>All hits in <paramref name="file"/> (for the preview window's highlight and navigation).</summary>
    public IReadOnlyList<SearchHit> HitsInFile(string file) => _hitsByFile.TryGetValue(file, out var list) ? list : [];

    public SearchRequest Snapshot() =>
        new()
        {
            Mode = Mode,
            Root = Root.Trim(),
            Pattern = Pattern,
            Language = Language,
            Strictness = Strictness,
            Backend = Backend,
            Rewrite = Rewrite,
            RuleSource = (RuleSourceKind)_ruleSourceIndex,
            RulePath = RulePath.Trim(),
            RuleIdFilter = RuleIdFilter,
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            Globs = Globs,
            ExcludeDirs = ExcludeDirs,
            Encoding = Encoding,
            ContextLines = (int)Math.Clamp(ContextLines ?? 0, 0, 50),
            MaxResults = (int)Math.Clamp(MaxResults ?? 0, 0, int.MaxValue),
            NoIgnore = NoIgnore,
            IncludeHidden = IncludeHidden,
        };

    public async Task SearchAsync(bool addToHistory = true)
    {
        CancelSearch();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var request = Snapshot();
        IsSearching = true;
        Status = T.StatusSearching;
        try
        {
            var outcome = await Task.Run(() => _search.Run(request, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            ApplyOutcome(request, outcome);
            if (addToHistory)
            {
                AddHistory(request.Pattern);
            }
        }
        catch (OperationCanceledException)
        {
            // CancelSearch already updated the status.
        }
        catch (Exception ex)
        {
            // UI boundary: surface the failure in the status bar instead of crashing the app.
            Status = T.StatusError + Describe(ex);
        }
        finally
        {
            if (_searchCts == cts)
            {
                _searchCts = null;
                IsSearching = false;
            }

            cts.Dispose();
        }
    }

    public void CancelSearch()
    {
        if (_searchCts is null)
        {
            return;
        }

        _searchCts.Cancel();
        _searchCts = null;
        IsSearching = false;
        Status = T.StatusCanceled;
    }

    public void UsePattern(string pattern) => Pattern = pattern;

    public string ResultsAsText() => ExportService.ToText(LastRequest ?? Snapshot(), VisibleHits, withHeader: false);

    /// <summary>The encoding the last search read files with, so rewrite offsets line up.</summary>
    public string? LastSearchEncoding()
    {
        if (LastRequest is null)
        {
            return null;
        }

        try
        {
            var root = SearchService.ResolveRoot(LastRequest.Root);
            return SearchService.EffectiveEncoding(LastRequest, SearchService.FindConfig(LastRequest, root));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void SaveSettings(double width, double height)
    {
        Settings.Last = Snapshot();
        Settings.History = History.ToList();
        Settings.UiLanguage = T.Code;
        Settings.Incremental = Incremental;
        Settings.WindowWidth = width;
        Settings.WindowHeight = height;
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Called while closing; there is no UI left to report to.
            Trace.WriteLine($"ag4-gui: failed to save settings to {_store.FilePath}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }

    public static string Describe(Exception ex) =>
        ex is AggregateException agg ? agg.Flatten().InnerExceptions[0].Message : ex.Message;

    private void ApplyOutcome(SearchRequest request, SearchOutcome outcome)
    {
        LastRequest = request;
        _hitsByFile = outcome
            .Hits.GroupBy(h => h.File)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Hits = outcome.Hits;
        Warnings = outcome.Warnings;
        RebuildFiles();
        Status =
            string.Format(
                CultureInfo.CurrentCulture,
                T.StatusDone,
                outcome.Hits.Count,
                _hitsByFile.Count,
                outcome.Elapsed.TotalSeconds
            ) + (outcome.Truncated ? T.StatusTruncated : "");
    }

    private void RebuildFiles()
    {
        var previous = _selectedFile?.File;
        var files = new List<FileGroup> { new(null, T.AllFiles, _hits.Count) };
        files.AddRange(_hitsByFile.Select(kv => new FileGroup(kv.Key, kv.Value[0].DisplayPath, kv.Value.Count)));
        Files = files;
        SelectedFile = files.FirstOrDefault(f => f.File == previous) ?? files[0];
        RefreshVisibleHits();
    }

    private void RefreshVisibleHits()
    {
        VisibleHits = _selectedFile?.File is { } file
            ? (_hitsByFile.TryGetValue(file, out var list) ? list : [])
            : _hits;
        CodeView = CodeViewBuilder.Build(VisibleHits, CodeViewMaxHits, T.CodeViewTruncated);
    }

    private void AddHistory(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        History.Remove(pattern);
        History.Insert(0, pattern);
        while (History.Count > AppSettings.MaxHistory)
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    private void ScheduleIncrementalSearch()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(IncrementalDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(Pattern))
            {
                await SearchAsync(addToHistory: false);
            }
        }
    }

    private void ToggleUiLanguage()
    {
        T = T.Code == "ja" ? UiStrings.En : UiStrings.Ja;
        if (_files.Count > 0)
        {
            RebuildFiles();
        }
    }

    private void RebuildLabels()
    {
        // Relabel in place: replacing a ComboBox's ItemsSource clears its selection.
        SetLabels(ModeLabels, [T.ModeAst, T.ModeRule, T.ModeText, T.ModeRegex]);
        SetLabels(RuleSourceLabels, [T.RuleSourceConfig, T.RuleSourceFile, T.RuleSourceInline]);

        static void SetLabels(IReadOnlyList<LabelItem> items, string[] labels)
        {
            for (var i = 0; i < items.Count; i++)
            {
                items[i].Label = labels[i];
            }
        }
    }

    private void LoadRequest(SearchRequest r)
    {
        _modeIndex = (int)r.Mode;
        _root = r.Root;
        _pattern = r.Pattern;
        _language = string.IsNullOrWhiteSpace(r.Language) ? SearchRequest.AutoLanguage : r.Language;
        _strictness = r.Strictness;
        _backend = r.Backend;
        _rewrite = r.Rewrite;
        _ruleSourceIndex = (int)r.RuleSource;
        _rulePath = r.RulePath;
        _ruleIdFilter = r.RuleIdFilter;
        _caseSensitive = r.CaseSensitive;
        _wholeWord = r.WholeWord;
        _globs = r.Globs;
        _excludeDirs = r.ExcludeDirs;
        _encoding = r.Encoding;
        _contextLines = r.ContextLines;
        _maxResults = r.MaxResults;
        _noIgnore = r.NoIgnore;
        _includeHidden = r.IncludeHidden;
    }

    private void LoadLanguagesInBackground()
    {
        // Listing languages instantiates every grammar, which is slow; keep it off the UI thread.
        _ = Task.Run(() =>
        {
            IReadOnlyList<string> ids;
            try
            {
                ids = SearchService.BuiltinLanguageIds();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Status = T.StatusError + Describe(ex));
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                // Only add missing ids (never Clear): removing the selected item makes the ComboBox drop
                // its selection. A saved non-builtin id (custom sgconfig language) stays in the list.
                var position = 1;
                foreach (var id in ids)
                {
                    var existing = Languages.IndexOf(id);
                    if (existing < 0)
                    {
                        Languages.Insert(Math.Min(position, Languages.Count), id);
                        existing = position;
                    }

                    position = existing + 1;
                }
            });
        });
    }
}
