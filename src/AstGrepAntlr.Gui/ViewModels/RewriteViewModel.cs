using System.Globalization;
using AstGrepAntlr.Gui.Infrastructure;
using AstGrepAntlr.Gui.Services;

namespace AstGrepAntlr.Gui.ViewModels;

public sealed class RewriteItem(RewritePreview preview) : ViewModelBase
{
    private bool _isSelected = preview.Error is null;
    private bool _applied;
    private string? _result = preview.Error;

    public RewritePreview Preview { get; } = preview;
    public string Label =>
        Preview.Error is null ? $"{Preview.DisplayPath}  ({Preview.EditCount})" : Preview.DisplayPath;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value && CanSelect);
    }

    public bool CanSelect => Preview.Error is null && !_applied;

    /// <summary>Error or "applied" note shown under the file name.</summary>
    public string? Result
    {
        get => _result;
        private set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(_result);

    public void MarkApplied()
    {
        _applied = true;
        IsSelected = false;
        Result = "✔";
        OnPropertyChanged(nameof(CanSelect));
    }

    public void MarkFailed(string error) => Result = error;
}

public sealed class RewriteViewModel : ViewModelBase
{
    private RewriteItem? _selectedItem;
    private string _summary = "";

    public RewriteViewModel(UiStrings t, IReadOnlyList<RewritePreview> previews)
    {
        T = t;
        Items = previews.Select(p => new RewriteItem(p)).ToList();
        _selectedItem = Items.Count > 0 ? Items[0] : null;
        _summary = Items.Count == 0 ? t.RewriteNothing : "";
        ApplyCommand = new RelayCommand(Apply);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    /// <summary>Raised after at least one file was written, so the caller can refresh its results.</summary>
    public event EventHandler? Applied;

    public UiStrings T { get; }
    public IReadOnlyList<RewriteItem> Items { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    public RewriteItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(DiffText));
            }
        }
    }

    public string DiffText => _selectedItem is null ? "" : _selectedItem.Preview.Error ?? _selectedItem.Preview.Diff;

    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    private void SetAll(bool selected)
    {
        foreach (var item in Items)
        {
            item.IsSelected = selected;
        }
    }

    private void Apply()
    {
        var applied = 0;
        var failed = 0;
        foreach (var item in Items.Where(i => i.IsSelected))
        {
            try
            {
                RewriteService.Apply(item.Preview);
                item.MarkApplied();
                applied++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                item.MarkFailed(ex.Message);
                failed++;
            }
        }

        var parts = new List<string> { string.Format(CultureInfo.CurrentCulture, T.RewriteApplied, applied) };
        if (failed > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, T.RewriteFailed, failed));
        }

        Summary = string.Join(" / ", parts);
        if (applied > 0)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }
}
