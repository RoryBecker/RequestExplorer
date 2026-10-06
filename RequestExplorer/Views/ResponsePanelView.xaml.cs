using System.ComponentModel;
using System.Windows;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

public partial class ResponsePanelView
{
    private ResponseViewModel? _subscribedResponse;
    private bool _isReady;

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

    private void OnBodySearchSelectionRequested()
    {
        if (_subscribedResponse is null || BodyTextBox is null)
        {
            return;
        }

        var start = _subscribedResponse.BodySearchSelectStart;
        var length = _subscribedResponse.BodySearchSelectLength;
        if (length <= 0 || start < 0 || start + length > BodyTextBox.Text.Length)
        {
            return;
        }

        BodyTextBox.Focus();
        BodyTextBox.Select(start, length);
        var line = BodyTextBox.GetLineIndexFromCharacterIndex(start);
        if (line >= 0)
        {
            BodyTextBox.ScrollToLine(line);
        }
    }
}
