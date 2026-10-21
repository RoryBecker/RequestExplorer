using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public partial class ResponseViewModel : ObservableObject
{
    private readonly List<int> _bodySearchMatches = [];
    private byte[]? _bodyBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyExpectationsFromLastRun))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyExpectationsFromLastRun))]
    private bool _isSending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyExpectationsFromLastRun))]
    private bool _isError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = "—";

    [ObservableProperty]
    private string _elapsedDisplay = "—";

    /// <summary>Raw total duration from the last result; null when unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyExpectationsFromLastRun))]
    private double? _totalMilliseconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearchBody))]
    [NotifyPropertyChangedFor(nameof(CanCopyExpectationsFromLastRun))]
    private string _bodyText = string.Empty;

    [ObservableProperty]
    private string _sizeWarning = string.Empty;

    [ObservableProperty]
    private string _timeToHeadersDisplay = "—";

    [ObservableProperty]
    private string _bodyReadDisplay = "—";

    [ObservableProperty]
    private string _totalTimingDisplay = "—";

    [ObservableProperty]
    private string _rawRequest = string.Empty;

    [ObservableProperty]
    private string _rawResponse = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveBodyCommand))]
    private bool _canSaveBody;

    [ObservableProperty]
    private bool _isDownloadable;

    [ObservableProperty]
    private string _suggestedFileName = "response.bin";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodySearchStatus))]
    private string _bodySearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodySearchStatus))]
    private int _bodySearchMatchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodySearchStatus))]
    private int _bodySearchMatchIndex = -1;

    [ObservableProperty]
    private int _bodySearchSelectStart;

    [ObservableProperty]
    private int _bodySearchSelectLength;

    public ObservableCollection<HeaderEntry> Headers { get; } = [];
    public ObservableCollection<string> Cookies { get; } = [];

    public bool CanSearchBody => !string.IsNullOrEmpty(BodyText);

    public bool CanCopyExpectationsFromLastRun =>
        HasResult && !IsSending && !IsError;

    public string BodySearchStatus =>
        string.IsNullOrEmpty(BodySearchQuery)
            ? string.Empty
            : BodySearchMatchCount == 0
                ? "No matches"
                : BodySearchMatchIndex < 0
                    ? $"{BodySearchMatchCount} match{(BodySearchMatchCount == 1 ? string.Empty : "es")}"
                    : $"{BodySearchMatchIndex + 1} of {BodySearchMatchCount}";

    /// <summary>Raised when the body search selection should be applied in the view.</summary>
    public event Action? BodySearchSelectionRequested;

    public void BeginSend()
    {
        IsSending = true;
        HasResult = true;
        IsError = false;
        ErrorMessage = string.Empty;
        SizeWarning = string.Empty;
        StatusDisplay = "Sending…";
        ElapsedDisplay = "—";
        TotalMilliseconds = null;
        BodyText = string.Empty;
        RawRequest = string.Empty;
        RawResponse = string.Empty;
        CanSaveBody = false;
        IsDownloadable = false;
        SuggestedFileName = "response.bin";
        _bodyBytes = null;
        Headers.Clear();
        Cookies.Clear();
        TotalTimingDisplay = "—";
        TimeToHeadersDisplay = "—";
        BodyReadDisplay = "—";
        ClearBodySearch();
    }

    public void Apply(HttpExecutionResult result)
    {
        IsSending = false;
        HasResult = true;
        IsError = !result.Succeeded;
        ErrorMessage = result.ErrorMessage ?? string.Empty;
        RawRequest = result.RawRequest ?? string.Empty;
        RawResponse = result.RawResponse ?? string.Empty;
        _bodyBytes = result.BodyBytes;
        CanSaveBody = result.Succeeded && result.BodyBytes is { Length: > 0 };
        IsDownloadable = result.IsDownloadable;
        SuggestedFileName = string.IsNullOrWhiteSpace(result.SuggestedFileName)
            ? "response.bin"
            : result.SuggestedFileName;

        Headers.Clear();
        foreach (var header in result.Headers)
        {
            Headers.Add(header);
        }

        Cookies.Clear();
        foreach (var cookie in result.Cookies)
        {
            Cookies.Add(cookie);
        }

        if (result.Succeeded)
        {
            StatusDisplay = $"{result.StatusCode} {result.ReasonPhrase}".Trim();
            BodyText = result.Body;
            SizeWarning = result.ExceededSoftSizeLimit
                ? $"Warning: response body is {FormatBytes(result.BodyByteCount)} (soft limit 5 MB)."
                : string.Empty;
        }
        else
        {
            StatusDisplay = "Error";
            BodyText = result.ErrorMessage ?? string.Empty;
            SizeWarning = string.Empty;
            CanSaveBody = false;
            IsDownloadable = false;
            _bodyBytes = null;
        }

        TotalMilliseconds = result.Timing.TotalMilliseconds;
        ElapsedDisplay = FormatMs(result.Timing.TotalMilliseconds);
        TotalTimingDisplay = FormatMs(result.Timing.TotalMilliseconds);
        TimeToHeadersDisplay = result.Succeeded
            ? FormatMs(result.Timing.TimeToHeadersMilliseconds)
            : "—";
        BodyReadDisplay = result.Succeeded
            ? FormatMs(result.Timing.BodyReadMilliseconds)
            : "—";

        RefreshBodySearch();
    }

    partial void OnBodySearchQueryChanged(string value) => RefreshBodySearch();

    [RelayCommand]
    private void FindNextBodyMatch() => MoveBodySearch(1);

    [RelayCommand]
    private void FindPreviousBodyMatch() => MoveBodySearch(-1);

    [RelayCommand(CanExecute = nameof(CanSaveBody))]
    private void SaveBody()
    {
        if (_bodyBytes is not { Length: > 0 })
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save response body",
            FileName = SuggestedFileName,
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, _bodyBytes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Save Response Body",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearBodySearch()
    {
        _bodySearchMatches.Clear();
        BodySearchMatchCount = 0;
        BodySearchMatchIndex = -1;
        BodySearchSelectStart = 0;
        BodySearchSelectLength = 0;
        OnPropertyChanged(nameof(BodySearchStatus));
        BodySearchSelectionRequested?.Invoke();
    }

    private void RefreshBodySearch()
    {
        _bodySearchMatches.Clear();
        var query = BodySearchQuery;
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(BodyText))
        {
            BodySearchMatchCount = 0;
            BodySearchMatchIndex = -1;
            BodySearchSelectStart = 0;
            BodySearchSelectLength = 0;
            OnPropertyChanged(nameof(BodySearchStatus));
            BodySearchSelectionRequested?.Invoke();
            return;
        }

        var start = 0;
        while (start < BodyText.Length)
        {
            var index = BodyText.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            _bodySearchMatches.Add(index);
            start = index + Math.Max(query.Length, 1);
        }

        BodySearchMatchCount = _bodySearchMatches.Count;
        BodySearchMatchIndex = -1;
        BodySearchSelectStart = 0;
        BodySearchSelectLength = 0;
        OnPropertyChanged(nameof(BodySearchStatus));
        BodySearchSelectionRequested?.Invoke();
    }

    private void MoveBodySearch(int delta)
    {
        if (_bodySearchMatches.Count == 0)
        {
            RefreshBodySearch();
            if (_bodySearchMatches.Count == 0)
            {
                return;
            }
        }

        var next = BodySearchMatchIndex + delta;
        if (next < 0)
        {
            next = _bodySearchMatches.Count - 1;
        }
        else if (next >= _bodySearchMatches.Count)
        {
            next = 0;
        }

        BodySearchMatchIndex = next;
        ApplyBodySearchSelection();
    }

    private void ApplyBodySearchSelection()
    {
        if (BodySearchMatchIndex < 0 || BodySearchMatchIndex >= _bodySearchMatches.Count)
        {
            return;
        }

        BodySearchSelectStart = _bodySearchMatches[BodySearchMatchIndex];
        BodySearchSelectLength = BodySearchQuery.Length;
        OnPropertyChanged(nameof(BodySearchStatus));
        BodySearchSelectionRequested?.Invoke();
    }

    private static string FormatMs(double ms) =>
        ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000.0:0.000} s";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.0} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.00} MB";
    }
}
