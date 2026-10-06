using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

public sealed class ExpectationCheckResult
{
    public bool IsMet { get; init; }

    public bool StatusFailed { get; init; }

    public bool TimingFailed { get; init; }

    public bool BodyFailed { get; init; }

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
    /// Timing is a maximum: actual total ms must be ≤ expected.
    /// </summary>
    public static bool IsMet(RequestNodeViewModel request, HttpExecutionResult result) =>
        Evaluate(request, result).IsMet;

    public static ExpectationCheckResult Evaluate(RequestNodeViewModel request, HttpExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var statusFailed = result.StatusCode is not int code || code != request.ExpectedStatusCode;

        var bodyFailed = false;
        if (request.ExpectBodyEnabled)
        {
            var actual = result.Succeeded ? (result.Body ?? string.Empty) : string.Empty;
            bodyFailed = !string.Equals(actual, request.ExpectedBody ?? string.Empty, StringComparison.Ordinal);
        }

        var timingFailed = false;
        if (request.ExpectTimingEnabled)
        {
            timingFailed = result.Timing.TotalMilliseconds > request.ExpectedTimingMilliseconds;
        }

        return new ExpectationCheckResult
        {
            StatusFailed = statusFailed,
            BodyFailed = bodyFailed,
            TimingFailed = timingFailed,
            IsMet = !statusFailed && !bodyFailed && !timingFailed
        };
    }
}
