using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

public partial class RunAllRequestResultRow : ObservableObject
{
    public required RequestNodeViewModel Request { get; init; }

    public string HierarchyPath => Request.HierarchyPath;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = "Pending";

    [ObservableProperty]
    private string _elapsedDisplay = "—";

    [ObservableProperty]
    private bool _succeeded;

    /// <summary>✓ when the response status matches the request's expected status code.</summary>
    [ObservableProperty]
    private string _metDisplay = string.Empty;

    public void SetExpectationMet(bool? met) =>
        MetDisplay = met == true ? "✓" : string.Empty;
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

    /// <summary>Request chosen via double-click; set before the dialog closes.</summary>
    public RequestNodeViewModel? NavigatedNode { get; private set; }

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

    private void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindRowItem(e.OriginalSource as DependencyObject) is not RunAllRequestResultRow row)
        {
            return;
        }

        e.Handled = true;
        NavigatedNode = row.Request;
        DialogResult = true;
    }

    private static RunAllRequestResultRow? FindRowItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DataGridRow { Item: RunAllRequestResultRow row })
            {
                return row;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
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
