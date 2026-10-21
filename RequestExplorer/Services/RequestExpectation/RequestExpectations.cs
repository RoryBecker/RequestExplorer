using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.RequestExpectation;

public sealed class ExpectationCheckResult
{
    public bool IsMet { get; init; }

    public bool StatusFailed { get; init; }

    public bool TimingFailed { get; init; }

    public bool BodyFailed { get; init; }

    /// <summary>True when the request has a body expectation enabled for this check.</summary>
    public bool BodyChecked { get; init; }

    /// <summary>True when timing was included in this check.</summary>
    public bool TimingChecked { get; init; }

    public static ExpectationCheckResult NotRun { get; } = new();

    public static ExpectationCheckResult UnmetWithoutResponse { get; } = new()
    {
        StatusFailed = true
    };
}

public static class RequestExpectations
{
    /// <summary>
    /// Status code always applies. Body and timing apply only when enabled on the request.
    /// Timing is also gated by <paramref name="includeTimingChecks"/> (Run All dialog toggle).
    /// Timing is a maximum: actual total ms must be ≤ expected.
    /// </summary>
    public static bool IsMet(
        RequestNodeViewModel request,
        HttpExecutionResult result,
        bool includeTimingChecks = true) =>
        Evaluate(request, result, includeTimingChecks).IsMet;

    public static ExpectationCheckResult Evaluate(
        RequestNodeViewModel request,
        HttpExecutionResult result,
        bool includeTimingChecks = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var statusFailed = result.StatusCode is not int code || code != request.ExpectedStatusCode;

        var bodyChecked = request.ExpectBodyEnabled;
        var bodyFailed = false;
        if (bodyChecked)
        {
            var actual = result.Succeeded ? (result.Body ?? string.Empty) : string.Empty;
            var expected = request.ExpectedBody ?? string.Empty;
            bodyFailed = request.ExpectBodyContains
                ? !actual.Contains(expected, StringComparison.Ordinal)
                : !string.Equals(actual, expected, StringComparison.Ordinal);
        }

        var timingChecked = includeTimingChecks && request.ExpectTimingEnabled;
        var timingFailed = false;
        if (timingChecked)
        {
            timingFailed = result.Timing.TotalMilliseconds > request.ExpectedTimingMilliseconds;
        }

        return new ExpectationCheckResult
        {
            StatusFailed = statusFailed,
            BodyChecked = bodyChecked,
            BodyFailed = bodyFailed,
            TimingChecked = timingChecked,
            TimingFailed = timingFailed,
            IsMet = !statusFailed && !bodyFailed && !timingFailed
        };
    }
}
