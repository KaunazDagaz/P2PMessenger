namespace P2PMessenger.Spikes.Nearby.Identification;

public static class Epochs
{
    public const int PeriodSeconds = 600;

    public static long Of(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;
}
