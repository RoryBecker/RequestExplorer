using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestExplorer.Views;

public partial class RunAllRequestResultRow : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = "Pending";

    [ObservableProperty]
    private string _elapsedDisplay = "—";

    [ObservableProperty]
    private bool _succeeded;
}

public partial class RunAllResultsDialog : Window
{
    private readonly Action? _cancelRequested;
    private bool _finished;
    private bool _stopRequested;

    public RunAllResultsDialog(
        string title,
        IEnumerable<RunAllRequestResultRow> rows,
        Action? cancelRequested = null)
    {
        InitializeComponent();
        Title = title;
        SummaryText.Text = "Starting…";
        ResultsGrid.ItemsSource = rows;
        _cancelRequested = cancelRequested;
        Closing += OnClosing;
    }

    public void SetSummary(string text) => SummaryText.Text = text;

    public void MarkFinished()
    {
        _finished = true;
        CancelButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.IsEnabled = true;
        CloseButton.IsDefault = true;
        CloseButton.IsCancel = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_finished || _stopRequested)
        {
            return;
        }

        _stopRequested = true;
        CancelButton.IsEnabled = false;
        SetSummary("Stopping remaining requests…");
        _cancelRequested?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished)
        {
            _cancelRequested?.Invoke();
        }
    }
}
