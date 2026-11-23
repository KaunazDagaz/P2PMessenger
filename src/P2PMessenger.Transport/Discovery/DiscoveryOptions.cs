using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Transport.Discovery;

public sealed record DiscoveryOptions
{
    public const int DefaultPort = 45821;

    private static readonly TimeSpan LongestTime = TimeSpan.FromDays(1);

    public int Port { get; init; } = DefaultPort;

    public IPAddress MulticastGroup { get; init; } = new([239, 255, 83, 5]);

    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan InterfaceRefreshInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HeardLifetime { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan SweepAfter { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(60);

    public int SweepBatchSize { get; init; } = 8;

    public TimeSpan SweepBatchPause { get; init; } = TimeSpan.FromMilliseconds(40);

    public int SweepMaxHosts { get; init; } = 510;

    public double ScreeningsPerSecond { get; init; } = 100;

    public int ScreeningBurst { get; init; } = 200;

    public double ScreeningsPerSecondPerSource { get; init; } = 10;

    public int ScreeningBurstPerSource { get; init; } = 20;

    public int MaxTrackedSources { get; init; } = 1024;

    public TimeSpan NonceMemory { get; init; } = TimeSpan.FromSeconds(30);

    internal string? FindProblem()
    {
        if (Port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return "The discovery port must be from 1 to 65535.";
        }

        if (MulticastGroup is null
            || MulticastGroup.AddressFamily != AddressFamily.InterNetwork
            || LocalInterface.ToValue(MulticastGroup) >> 28 != 0xE)
        {
            return "The multicast group must be an IPv4 multicast address.";
        }

        TimeSpan[] positive = [AnnounceInterval, InterfaceRefreshInterval, HeardLifetime, SweepAfter, SweepInterval, NonceMemory];
        if (Array.Exists(positive, static time => time <= TimeSpan.Zero || time > LongestTime)
            || SweepBatchPause < TimeSpan.Zero
            || SweepBatchPause > LongestTime)
        {
            return "Every discovery time must be positive and at most one day; the sweep pause may be zero.";
        }

        if (HeardLifetime <= AnnounceInterval)
        {
            return "A contact must stay heard for longer than one announcement interval.";
        }

        if (SweepBatchSize < 1 || SweepMaxHosts < 0 || ScreeningBurst < 1 || ScreeningBurstPerSource < 1 || MaxTrackedSources < 1)
        {
            return "Every discovery count must be at least 1, and the sweep size at least 0.";
        }

        if (!IsPositiveRate(ScreeningsPerSecond) || !IsPositiveRate(ScreeningsPerSecondPerSource))
        {
            return "Every screening rate must be a positive number.";
        }

        return null;
    }

    private static bool IsPositiveRate(double rate) => double.IsFinite(rate) && rate > 0;
}
