namespace P2PMessenger.Application.Delivery;

public sealed record DeliveryOptions
{
    private static readonly TimeSpan LongestTime = TimeSpan.FromDays(1);

    public TimeSpan WakeUpInterval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ResendAfter { get; init; } = TimeSpan.FromSeconds(30);

    internal string? FindProblem() =>
        WakeUpInterval <= TimeSpan.Zero || WakeUpInterval > LongestTime || ResendAfter <= TimeSpan.Zero || ResendAfter > LongestTime
            ? "Every time must be positive and at most one day."
            : null;
}
