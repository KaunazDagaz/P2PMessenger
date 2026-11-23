using System.Net;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class NetworkSnapshotTests
{
    [Fact]
    [Trait("Story", "US-06")]
    public void Create_CellularVpnLoopbackAndLinkLocal_AreLeftOutOfTheLocalInterfaces()
    {
        LocalInterface[] candidates =
        [
            new("wlan0", 3, IPAddress.Parse("10.91.108.65"), 24, true),
            new("rmnet_data1", 4, IPAddress.Parse("100.76.12.9"), 30, false),
            new("ccmni0", 5, IPAddress.Parse("10.12.0.2"), 24, false),
            new("v4-rmnet1", 6, IPAddress.Parse("192.0.0.4"), 29, false),
            new("tun0", 7, IPAddress.Parse("10.8.0.2"), 24, false),
            new("wg0", 8, IPAddress.Parse("10.9.0.2"), 24, false),
            new("lo", 1, IPAddress.Loopback, 8, false),
            new("eth1", 9, IPAddress.Parse("169.254.10.10"), 16, true),
            new("eth2", 10, IPAddress.Parse("192.168.5.5"), 32, true),
        ];

        var snapshot = NetworkSnapshot.Create(candidates, hasOtherNetwork: false);

        var local = Assert.Single(snapshot.Interfaces);
        Assert.Equal("wlan0", local.Name);
        Assert.True(snapshot.HasOtherNetwork);
        Assert.True(snapshot.HasAnyNetwork);
    }

    [Fact]
    [Trait("Story", "US-07")]
    public void Create_NothingButLoopback_HasNoNetworkAtAll()
    {
        var snapshot = NetworkSnapshot.Create([new LocalInterface("lo", 1, IPAddress.Loopback, 8, false)], hasOtherNetwork: false);

        Assert.Empty(snapshot.Interfaces);
        Assert.False(snapshot.HasOtherNetwork);
        Assert.False(snapshot.HasAnyNetwork);
        Assert.False(NetworkSnapshot.None.HasAnyNetwork);
    }

    [Fact]
    public void Create_SameAddressTwice_IsKeptOnceAndTheListIsSorted()
    {
        var snapshot = NetworkSnapshot.Create(
            [
                new LocalInterface("wlan0", 3, IPAddress.Parse("192.168.1.20"), 24, true),
                new LocalInterface("eth0", 2, IPAddress.Parse("10.0.0.5"), 8, true),
                new LocalInterface("wlan0", 3, IPAddress.Parse("192.168.1.20"), 24, true),
            ],
            hasOtherNetwork: false);

        Assert.Equal(["eth0", "wlan0"], snapshot.Interfaces.Select(static item => item.Name));
    }

    [Fact]
    public void IsSameAs_ComparesTheInterfacesAndTheOtherNetworks()
    {
        LocalInterface wlan = new("wlan0", 3, IPAddress.Parse("192.168.1.20"), 24, true);

        Assert.True(NetworkSnapshot.Create([wlan], false).IsSameAs(NetworkSnapshot.Create([wlan], false)));
        Assert.False(NetworkSnapshot.Create([wlan], false).IsSameAs(NetworkSnapshot.Create([wlan], true)));
        Assert.False(NetworkSnapshot.Create([wlan], false).IsSameAs(NetworkSnapshot.None));
    }

    [Fact]
    public void SystemInterfaceSource_Read_GivesOnlyUsableIPv4Interfaces()
    {
        var snapshot = new SystemInterfaceSource().Read();

        Assert.All(snapshot.Interfaces, static item =>
        {
            Assert.True(NetworkSnapshot.IsLocal(item));
            Assert.False(IPAddress.IsLoopback(item.Address));
        });
    }
}
