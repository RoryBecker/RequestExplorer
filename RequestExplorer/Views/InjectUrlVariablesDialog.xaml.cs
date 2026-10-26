using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Services.Variables;

namespace RequestExplorer.Views;

public partial class InjectUrlVariablesDialog : Window
{
    public InjectUrlVariablesDialog(string address, IReadOnlyList<UrlVariableInjector.Match> matches)
    {
        InitializeComponent();
        ViewModel = new InjectUrlVariablesViewModel(address, matches);
        DataContext = ViewModel;
    }

    public InjectUrlVariablesViewModel ViewModel { get; }

    public string ResultAddress => ViewModel.PreviewAddress;

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

public sealed partial class InjectUrlVariablesViewModel : ObservableObject
{
    private readonly string _address;
    private readonly IReadOnlyList<UrlVariableInjector.Match> _matches;
    private readonly Dictionary<string, UrlVariableInjector.Match> _byName;

    public InjectUrlVariablesViewModel(string address, IReadOnlyList<UrlVariableInjector.Match> matches)
    {
        _address = address ?? string.Empty;
        _matches = matches.Where(m => m.Spans.Count > 0).ToList();
        _byName = _matches.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var suggested = UrlVariableInjector.SuggestSelection(_matches);
        Rows = new ObservableCollection<InjectUrlVariableRow>(
            _matches.Select(m => new InjectUrlVariableRow(m, suggested.Contains(m.Name))));

        foreach (var row in Rows)
        {
            row.PropertyChanged += Row_OnPropertyChanged;
        }

        RefreshPreview();
    }

    public ObservableCollection<InjectUrlVariableRow> Rows { get; }

    [ObservableProperty]
    private string _previewAddress = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ResolvedTextSpan> _previewSpans = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string _conflictMessage = string.Empty;

    [ObservableProperty]
    private bool _canConfirm;

    public bool HasConflict => !string.IsNullOrEmpty(ConflictMessage);

    private void Row_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InjectUrlVariableRow.IsChecked))
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        var selected = Rows
            .Where(r => r.IsChecked && _byName.TryGetValue(r.Name, out _))
            .Select(r => _byName[r.Name])
            .ToList();

        if (UrlVariableInjector.SelectionConflicts(selected))
        {
            PreviewAddress = _address;
            PreviewSpans = [new ResolvedTextSpan(_address, IsFromVariable: false)];
            ConflictMessage = "These replacements overlap. Untick one of the conflicting variables.";
            CanConfirm = false;
            return;
        }

        ConflictMessage = string.Empty;
        var applied = UrlVariableInjector.ApplyWithSpans(_address, selected);
        PreviewAddress = applied.Text;
        PreviewSpans = applied.Spans;
        CanConfirm = !string.Equals(PreviewAddress, _address, StringComparison.Ordinal);
    }
}

public sealed partial class InjectUrlVariableRow : ObservableObject
{
    public InjectUrlVariableRow(UrlVariableInjector.Match match, bool isChecked)
    {
        Name = match.Name;
        Value = match.Value;
        MatchSummary = match.Spans.Count == 1
            ? $"1 · {match.ReplacedLength} chars"
            : $"{match.Spans.Count} · {match.ReplacedLength} chars";
        _isChecked = isChecked;
    }

    public string Name { get; }

    public string Value { get; }

    public string MatchSummary { get; }

    [ObservableProperty]
    private bool _isChecked;
}
