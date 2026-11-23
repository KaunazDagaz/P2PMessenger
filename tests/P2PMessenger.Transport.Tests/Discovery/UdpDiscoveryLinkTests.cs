using System.Net;
using System.Net.Sockets;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class UdpDiscoveryLinkTests
{
    private static readonly IPAddress Group = IPAddress.Parse("239.255.83.5");
    private static readonly LocalInterface Loopback = new("lo", 1, IPAddress.Loopback, 8, supportsMulticast: false);

    [Fact(Timeout = 30_000)]
    public async Task SendAsync_UnicastFromTheInterface_IsReceivedWithTheSendersAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        int port = FreeUdpPort();
        await using var link = new UdpDiscoveryLink(port, Group);
        Assert.Equal(SocketError.Success, link.Open());
        link.UseInterfaces([Loopback]);
        byte[] datagram = [.. Enumerable.Range(0, 274).Select(static value => (byte)value)];
        var buffer = new byte[512];

        var receiving = link.ReceiveAsync(buffer, cancellationToken).AsTask();
        Assert.Equal(SocketError.Success, await link.SendAsync(Loopback, new IPEndPoint(IPAddress.Loopback, port), datagram, sweep: false, cancellationToken));
        Assert.Equal(SocketError.Success, await link.SendAsync(Loopback, new IPEndPoint(IPAddress.Loopback, port), datagram, sweep: true, cancellationToken));
        var received = await receiving;

        Assert.Equal(274, received.Length);
        Assert.Equal(IPAddress.Loopback, received.Source);
        Assert.Equal(datagram, buffer[..274]);
        Assert.Equal(274, (await link.ReceiveAsync(buffer, cancellationToken)).Length);
    }

    [Fact]
    public async Task Open_TwoAppsOnOneComputer_ShareTheDiscoveryPort()
    {
        int port = FreeUdpPort();
        await using var first = new UdpDiscoveryLink(port, Group);
        await using var second = new UdpDiscoveryLink(port, Group);

        Assert.Equal(SocketError.Success, first.Open());
        Assert.Equal(SocketError.Success, second.Open());
    }

    [Fact(Timeout = 30_000)]
    public async Task DisposeAsync_EndsAWaitingReceiveAndRefusesSends()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = new UdpDiscoveryLink(FreeUdpPort(), Group);
        Assert.Equal(SocketError.Success, link.Open());
        var receiving = link.ReceiveAsync(new byte[512], cancellationToken).AsTask();

        await link.DisposeAsync();

        Assert.True((await receiving).IsClosed);
        Assert.True((await link.ReceiveAsync(new byte[512], cancellationToken)).IsClosed);
        Assert.Equal(SocketError.Shutdown, await link.SendAsync(Loopback, new IPEndPoint(IPAddress.Loopback, 9), new byte[4], sweep: false, cancellationToken));
        Assert.Equal(SocketError.Shutdown, link.Open());
        link.UseInterfaces([Loopback]);
        await link.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_FromAnAddressThatIsNotLocal_ReturnsTheErrorInsteadOfThrowing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var link = new UdpDiscoveryLink(FreeUdpPort(), Group);
        var foreign = new LocalInterface("eth9", 9, IPAddress.Parse("192.0.2.77"), 24, supportsMulticast: true);

        var result = await link.SendAsync(foreign, new IPEndPoint(IPAddress.Parse("192.0.2.255"), 45821), new byte[4], sweep: false, cancellationToken);

        Assert.NotEqual(SocketError.Success, result);
    }

    [Fact]
    public async Task ReceiveAsync_BeforeOpen_SaysTheLinkIsClosed()
    {
        await using var link = new UdpDiscoveryLink(FreeUdpPort(), Group);

        Assert.True((await link.ReceiveAsync(new byte[512], TestContext.Current.CancellationToken)).IsClosed);
    }

    [Fact]
    public void Constructor_PortOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpDiscoveryLink(0, Group));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpDiscoveryLink(65_536, Group));
    }

    private static int FreeUdpPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }
}
