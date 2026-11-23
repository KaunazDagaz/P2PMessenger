using System.Net;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class DiscoveryOptionsTests
{
    [Fact]
    public void Defaults_AreValidAndMatchTheDesign()
    {
        var options = new DiscoveryOptions();

        Assert.Null(options.FindProblem());
        Assert.Equal(45821, options.Port);
        Assert.Equal(IPAddress.Parse("239.255.83.5"), options.MulticastGroup);
        Assert.Equal(TimeSpan.FromSeconds(3), options.AnnounceInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.HeardLifetime);
        Assert.Equal(510, options.SweepMaxHosts);
    }

    public static TheoryData<DiscoveryOptions> Invalid() =>
    [
        new DiscoveryOptions { Port = 0 },
        new DiscoveryOptions { Port = 65536 },
        new DiscoveryOptions { MulticastGroup = IPAddress.Parse("192.168.1.255") },
        new DiscoveryOptions { MulticastGroup = IPAddress.IPv6Loopback },
        new DiscoveryOptions { AnnounceInterval = TimeSpan.Zero },
        new DiscoveryOptions { InterfaceRefreshInterval = TimeSpan.FromDays(2) },
        new DiscoveryOptions { HeardLifetime = TimeSpan.FromSeconds(3) },
        new DiscoveryOptions { SweepBatchPause = TimeSpan.FromMilliseconds(-1) },
        new DiscoveryOptions { SweepBatchSize = 0 },
        new DiscoveryOptions { SweepMaxHosts = -1 },
        new DiscoveryOptions { ScreeningsPerSecond = double.NaN },
        new DiscoveryOptions { ScreeningsPerSecondPerSource = 0 },
        new DiscoveryOptions { ScreeningBurst = 0 },
        new DiscoveryOptions { MaxTrackedSources = 0 },
        new DiscoveryOptions { NonceMemory = TimeSpan.Zero },
    ];

    [Theory]
    [MemberData(nameof(Invalid))]
    public void FindProblem_InvalidValue_IsReported(DiscoveryOptions options)
    {
        Assert.NotNull(options.FindProblem());
    }

    [Fact]
    public void FindProblem_NoPauseBetweenSweepBatches_IsAllowed()
    {
        Assert.Null(new DiscoveryOptions { SweepBatchPause = TimeSpan.Zero }.FindProblem());
    }
}
