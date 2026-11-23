using System.Net;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class LocalInterfaceTests
{
    [Fact]
    public void Subnet_OfAHotspotAddress_HasItsNetworkBroadcastAndHosts()
    {
        var item = new LocalInterface("wlan0", 3, IPAddress.Parse("10.91.108.65"), 24, supportsMulticast: true);

        Assert.Equal(IPAddress.Parse("10.91.108.0"), item.Network);
        Assert.Equal(IPAddress.Parse("10.91.108.255"), item.Broadcast);
        Assert.Equal(254, item.HostCount);
        var others = item.OtherHosts().ToList();
        Assert.Equal(253, others.Count);
        Assert.Equal(IPAddress.Parse("10.91.108.1"), others[0]);
        Assert.Equal(IPAddress.Parse("10.91.108.254"), others[^1]);
        Assert.DoesNotContain(item.Address, others);
        Assert.True(item.Contains(IPAddress.Parse("10.91.108.200")));
        Assert.False(item.Contains(IPAddress.Parse("10.91.109.1")));
        Assert.Equal("wlan0 10.91.108.65/24", item.ToString());
    }

    [Theory]
    [InlineData(31, 0)]
    [InlineData(32, 0)]
    [InlineData(30, 2)]
    [InlineData(23, 510)]
    public void HostCount_DependsOnThePrefix(int prefix, long hosts)
    {
        var item = new LocalInterface("eth0", 1, IPAddress.Parse("192.168.10.1"), prefix, supportsMulticast: false);

        Assert.Equal(hosts, item.HostCount);
        Assert.Equal((int)Math.Max(0, hosts - 1), item.OtherHosts().Count());
    }

    [Fact]
    public void Constructor_NotIPv4OrPrefixOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LocalInterface("eth0", 1, IPAddress.IPv6Loopback, 64, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.2"), 33, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.2"), -1, false));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(
            new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.2"), 24, true),
            new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.2"), 24, true));
        Assert.NotEqual(
            new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.2"), 24, true),
            new LocalInterface("eth0", 1, IPAddress.Parse("192.168.1.3"), 24, true));
    }
}
