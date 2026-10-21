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
