namespace P2PMessenger.Application.Delivery;

public sealed record DeliveryOptions
{
    public const int MaxPassOnBatchSize = 1024;
    public const int MaxPassOnRoundLimit = 10_000;

    private static readonly TimeSpan LongestTime = TimeSpan.FromDays(1);

    public TimeSpan WakeUpInterval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ResendAfter { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PassOnSummaryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public int PassOnBatchSize { get; init; } = 16;

    public int PassOnRoundLimit { get; init; } = 256;

    internal string? FindProblem()
    {
        if (WakeUpInterval <= TimeSpan.Zero || WakeUpInterval > LongestTime
            || ResendAfter <= TimeSpan.Zero || ResendAfter > LongestTime
            || PassOnSummaryDelay <= TimeSpan.Zero || PassOnSummaryDelay > LongestTime)
        {
            return "Every time must be positive and at most one day.";
        }

        if (PassOnBatchSize is < 1 or > MaxPassOnBatchSize || PassOnRoundLimit is < 1 or > MaxPassOnRoundLimit)
        {
            return "Pass-on limits must be positive and within bounds.";
        }

        return null;
    }
}
