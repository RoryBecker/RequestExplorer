using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

public partial class ApplyVariableToUrlsDialog : Window
{
    public ApplyVariableToUrlsDialog(
        string variableName,
        IReadOnlyList<UrlVariableTreeApplicator.RequestMatch> matches)
    {
        InitializeComponent();
        ViewModel = new ApplyVariableToUrlsViewModel(variableName, matches);
        DataContext = ViewModel;
    }

    public ApplyVariableToUrlsViewModel ViewModel { get; }

    public IReadOnlyList<UrlVariableTreeApplicator.RequestMatch> SelectedMatches =>
        ViewModel.SelectedMatches;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanConfirm)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public sealed partial class ApplyVariableToUrlsViewModel : ObservableObject
{
    public ApplyVariableToUrlsViewModel(
        string variableName,
        IReadOnlyList<UrlVariableTreeApplicator.RequestMatch> matches)
    {
        var name = variableName?.Trim() ?? string.Empty;
        IntroText = string.IsNullOrEmpty(name)
            ? "Requests whose URLs contain this variable's value. Uncheck any to skip."
            : $"Requests whose URLs contain {{{{{name}}}}}. Uncheck any to skip.";

        Rows = new ObservableCollection<ApplyVariableToUrlRow>(
            matches.Select(m => new ApplyVariableToUrlRow(m)));
        ShadowSkipCount = Rows.Count(r => r.IsShadowed);
        HasShadowedSkips = ShadowSkipCount > 0;

        foreach (var row in Rows)
        {
            row.PropertyChanged += Row_OnPropertyChanged;
        }

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        SelectedRow = Rows.FirstOrDefault(r => !r.IsShadowed) ?? Rows.FirstOrDefault();
        RefreshCanConfirm();
    }

    public string IntroText { get; }

    public ObservableCollection<ApplyVariableToUrlRow> Rows { get; }

    public ICollectionView RowsView { get; }

    public int ShadowSkipCount { get; }

    public bool HasShadowedSkips { get; }

    public IReadOnlyList<UrlVariableTreeApplicator.RequestMatch> SelectedMatches =>
        Rows.Where(r => r.IsChecked && !r.IsShadowed).Select(r => r.Match).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSpans))]
    private ApplyVariableToUrlRow? _selectedRow;

    [ObservableProperty]
    private bool _canConfirm;

    [ObservableProperty]
    private bool _showShadowedSkips;

    public IReadOnlyList<ResolvedTextSpan> PreviewSpans =>
        SelectedRow is { IsShadowed: false } row
            ? row.Match.PreviewSpans
            : [];

    partial void OnShowShadowedSkipsChanged(bool value)
    {
        RowsView.Refresh();
        if (SelectedRow is null || (!value && SelectedRow.IsShadowed))
        {
            SelectedRow = Rows.FirstOrDefault(r => !r.IsShadowed) ?? Rows.FirstOrDefault();
        }
    }

    private bool FilterRow(object item) =>
        item is ApplyVariableToUrlRow row && (ShowShadowedSkips || !row.IsShadowed);

    private void Row_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplyVariableToUrlRow.IsChecked))
        {
            RefreshCanConfirm();
        }
    }

    private void RefreshCanConfirm() =>
        CanConfirm = Rows.Any(r => r.IsChecked && !r.IsShadowed);
}

public sealed partial class ApplyVariableToUrlRow : ObservableObject
{
    public ApplyVariableToUrlRow(UrlVariableTreeApplicator.RequestMatch match)
    {
        Match = match;
        Path = match.Request.HierarchyPath;
        Address = match.OriginalAddress;
        IsShadowed = match.IsShadowed;
        ShadowReason = match.ShadowReason;
        MatchSummary = match.IsShadowed
            ? "Shadowed"
            : match.Match.Spans.Count == 1
                ? $"1 · {match.Match.ReplacedLength} chars"
                : $"{match.Match.Spans.Count} · {match.Match.ReplacedLength} chars";
        _isChecked = !match.IsShadowed;
        ToolTip = match.IsShadowed
            ? match.ShadowReason
            : match.Request.HierarchyPath;
    }

    public UrlVariableTreeApplicator.RequestMatch Match { get; }

    public string Path { get; }

    public string Address { get; }

    public string MatchSummary { get; }

    public bool IsShadowed { get; }

    public string ShadowReason { get; }

    public string ToolTip { get; }

    public bool CanCheck => !IsShadowed;

    public RequestNodeViewModel Request => Match.Request;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (IsShadowed && value)
        {
            IsChecked = false;
        }
    }
}
