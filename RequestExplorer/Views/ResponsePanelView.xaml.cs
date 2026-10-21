using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RequestExplorer.ViewModels;
using RequestExplorer.Views.OtherHelpers;

namespace RequestExplorer.Views;

public partial class ResponsePanelView
{
    private ResponseViewModel? _subscribedResponse;
    private bool _isReady;
    private int _expectedBodyFindFromIndex;

    public ResponsePanelView()
    {
        InitializeComponent();
        _isReady = true;
        UpdateTabVisibility();
    }

    private void Tab_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_isReady)
        {
            return;
        }

        UpdateTabVisibility();
    }

    private void ResponsePanelView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedResponse is not null)
        {
            _subscribedResponse.BodySearchSelectionRequested -= OnBodySearchSelectionRequested;
            _subscribedResponse.PropertyChanged -= OnResponsePropertyChanged;
            _subscribedResponse = null;
        }

        if (e.NewValue is ResponseViewModel response)
        {
            _subscribedResponse = response;
            _subscribedResponse.BodySearchSelectionRequested += OnBodySearchSelectionRequested;
            _subscribedResponse.PropertyChanged += OnResponsePropertyChanged;
        }

        if (!_isReady)
        {
            return;
        }

        UpdateTabVisibility();
    }

    private void OnResponsePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResponseViewModel.HasResult)
            or nameof(ResponseViewModel.IsSending))
        {
            UpdateTabVisibility();
        }
    }

    private void UpdateTabVisibility()
    {
        if (!_isReady
            || EmptyMessage is null
            || TabStrip is null
            || BodyPanel is null
            || HeadersPanel is null
            || CookiesPanel is null
            || RawResponseTextBox is null
            || TimingPanel is null
            || ExpectationsPanel is null)
        {
            return;
        }

        var hasResponseVm = _subscribedResponse is not null;
        var hasResult = _subscribedResponse?.HasResult == true || _subscribedResponse?.IsSending == true;
        var showExpectations = hasResponseVm && TabExpectations.IsChecked == true;

        // Expectations is request config — available even before a response exists.
        TabStrip.Visibility = hasResponseVm ? Visibility.Visible : Visibility.Collapsed;
        EmptyMessage.Visibility = hasResponseVm && !hasResult && !showExpectations
            ? Visibility.Visible
            : Visibility.Collapsed;

        var showBody = hasResult && TabBody.IsChecked == true;
        var showHeaders = hasResult && TabHeaders.IsChecked == true;
        var showCookies = hasResult && TabCookies.IsChecked == true;
        var showRaw = hasResult && TabRawResponse.IsChecked == true;
        var showTiming = hasResult && TabTiming.IsChecked == true;

        BodyPanel.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        HeadersPanel.Visibility = showHeaders ? Visibility.Visible : Visibility.Collapsed;
        CookiesPanel.Visibility = showCookies ? Visibility.Visible : Visibility.Collapsed;
        RawResponseTextBox.Visibility = showRaw ? Visibility.Visible : Visibility.Collapsed;
        TimingPanel.Visibility = showTiming ? Visibility.Visible : Visibility.Collapsed;
        ExpectationsPanel.Visibility = showExpectations ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BodySearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_subscribedResponse is null)
        {
            return;
        }

        TextFindHighlight.TryHandleFindKeys(
            e,
            () => _subscribedResponse.FindNextBodyMatchCommand.Execute(null),
            () => _subscribedResponse.FindPreviousBodyMatchCommand.Execute(null));
    }

    private void OnBodySearchSelectionRequested()
    {
        if (_subscribedResponse is null)
        {
            return;
        }

        TextFindHighlight.Apply(
            BodyTextBox,
            _subscribedResponse.BodySearchSelectStart,
            _subscribedResponse.BodySearchSelectLength);
    }

    private void ExpectedBodyMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (KeepExpectedBodySelectionMenuItem is null)
        {
            return;
        }

        var box = sender is ContextMenu { PlacementTarget: TextBox target }
            ? target
            : ExpectedBodyTextBox;
        KeepExpectedBodySelectionMenuItem.IsEnabled = box is not null && box.SelectionLength > 0;
    }

    private void KeepExpectedBodySelection_OnClick(object sender, RoutedEventArgs e)
    {
        var box = sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextBox target } }
            ? target
            : ExpectedBodyTextBox;
        if (box is null || box.SelectionLength <= 0)
        {
            return;
        }

        var kept = box.SelectedText;
        box.Text = kept;
        box.CaretIndex = kept.Length;
        _expectedBodyFindFromIndex = 0;
        UpdateExpectedBodyFindStatus();
    }

    private void ExpectedBodyFindBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _expectedBodyFindFromIndex = 0;
        UpdateExpectedBodyFindStatus();
    }

    private void ExpectedBodyTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateExpectedBodyFindStatus();
    }

    private void ExpectedBodyFindNext_OnClick(object sender, RoutedEventArgs e) =>
        MoveExpectedBodyFind(1);

    private void ExpectedBodyFindPrevious_OnClick(object sender, RoutedEventArgs e) =>
        MoveExpectedBodyFind(-1);

    private void MoveExpectedBodyFind(int direction)
    {
        if (ExpectedBodyFindBox is null || ExpectedBodyTextBox is null)
        {
            return;
        }

        var query = ExpectedBodyFindBox.Text ?? string.Empty;
        var text = ExpectedBodyTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(query) || text.Length == 0)
        {
            UpdateExpectedBodyFindStatus();
            return;
        }

        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        int found;
        if (direction >= 0)
        {
            var start = Math.Clamp(_expectedBodyFindFromIndex, 0, text.Length);
            found = text.IndexOf(query, start, comparison);
            if (found < 0 && start > 0)
            {
                found = text.IndexOf(query, 0, comparison);
            }
        }
        else
        {
            var start = Math.Clamp(_expectedBodyFindFromIndex - 1, 0, Math.Max(0, text.Length - 1));
            found = start >= 0 && text.Length > 0
                ? text.LastIndexOf(query, start, comparison)
                : -1;
            if (found < 0 && _expectedBodyFindFromIndex > 0)
            {
                found = text.LastIndexOf(query, text.Length - 1, comparison);
            }
        }

        if (found < 0)
        {
            if (ExpectedBodyFindStatusText is not null)
            {
                ExpectedBodyFindStatusText.Text = "No matches";
            }

            return;
        }

        TextFindHighlight.Apply(ExpectedBodyTextBox, found, query.Length);
        _expectedBodyFindFromIndex = direction >= 0 ? found + query.Length : found;
        UpdateExpectedBodyFindStatus(found);
    }

    private void ExpectedBodyFindBox_OnPreviewKeyDown(object sender, KeyEventArgs e) =>
        TextFindHighlight.TryHandleFindKeys(e, () => MoveExpectedBodyFind(1), () => MoveExpectedBodyFind(-1));

    private void UpdateExpectedBodyFindStatus(int? currentIndex = null)
    {
        if (ExpectedBodyFindBox is null
            || ExpectedBodyTextBox is null
            || ExpectedBodyFindStatusText is null)
        {
            return;
        }

        var query = ExpectedBodyFindBox.Text ?? string.Empty;
        var text = ExpectedBodyTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            ExpectedBodyFindStatusText.Text = string.Empty;
            return;
        }

        var count = 0;
        var index = 0;
        var matchOrdinal = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            count++;
            if (currentIndex is int cur && found == cur)
            {
                matchOrdinal = count;
            }

            index = found + Math.Max(1, query.Length);
        }

        ExpectedBodyFindStatusText.Text = count == 0
            ? "No matches"
            : matchOrdinal > 0
                ? $"{matchOrdinal} of {count}"
                : $"{count} match{(count == 1 ? string.Empty : "es")}";
    }
}
