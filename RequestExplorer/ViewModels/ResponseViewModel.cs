using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public partial class ResponseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = "—";

    [ObservableProperty]
    private string _elapsedDisplay = "—";

    [ObservableProperty]
    private string _bodyText = string.Empty;

    [ObservableProperty]
    private string _sizeWarning = string.Empty;

    [ObservableProperty]
    private string _timeToHeadersDisplay = "—";

    [ObservableProperty]
    private string _bodyReadDisplay = "—";

    [ObservableProperty]
    private string _totalTimingDisplay = "—";

    public ObservableCollection<HeaderEntry> Headers { get; } = [];
    public ObservableCollection<string> Cookies { get; } = [];

    public void BeginSend()
    {
        IsSending = true;
        HasResult = true;
        IsError = false;
        ErrorMessage = string.Empty;
        SizeWarning = string.Empty;
        StatusDisplay = "Sending…";
        ElapsedDisplay = "—";
        BodyText = string.Empty;
        Headers.Clear();
        Cookies.Clear();
        TotalTimingDisplay = "—";
        TimeToHeadersDisplay = "—";
        BodyReadDisplay = "—";
    }

    public void Apply(HttpExecutionResult result)
    {
        IsSending = false;
        HasResult = true;
        IsError = !result.Succeeded;
        ErrorMessage = result.ErrorMessage ?? string.Empty;

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
        }

        ElapsedDisplay = FormatMs(result.Timing.TotalMilliseconds);
        TotalTimingDisplay = FormatMs(result.Timing.TotalMilliseconds);
        TimeToHeadersDisplay = result.Succeeded
            ? FormatMs(result.Timing.TimeToHeadersMilliseconds)
            : "—";
        BodyReadDisplay = result.Succeeded
            ? FormatMs(result.Timing.BodyReadMilliseconds)
            : "—";
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
