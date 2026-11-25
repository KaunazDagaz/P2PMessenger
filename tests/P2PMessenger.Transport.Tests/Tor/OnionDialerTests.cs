using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Identity;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Story", "US-09")]
public sealed class OnionDialerTests
{
    [Fact]
    public void Constructor_ProxyNotOnLoopback_Throws()
    {
        using var keys = KeySet.Create();

        Assert.Throws<ArgumentException>(() => new OnionDialer(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9050), keys.OnionAddress, NullLogger.Instance));
    }

    [Fact]
    public void Route_IsTor()
    {
        using var keys = KeySet.Create();

        var dialer = new OnionDialer(new IPEndPoint(IPAddress.Loopback, 9050), keys.OnionAddress, NullLogger.Instance);

        Assert.Equal(ConnectionRoute.Tor, dialer.Route);
        Assert.Equal(SocksFailure.None, dialer.LastFailure);
    }

    [Fact]
    public async Task DialAsync_NothingListensOnTheProxyPort_ReportsTheProxyUnreachable()
    {
        using var keys = KeySet.Create();
        var dialer = new OnionDialer(FakeSocksProxy.Unused(), keys.OnionAddress, NullLogger.Instance);

        var stream = await dialer.DialAsync(TestContext.Current.CancellationToken);

        Assert.Null(stream);
        Assert.Equal(SocksFailure.ProxyUnreachable, dialer.LastFailure);
    }

    [Fact]
    public async Task DialAsync_NoDescriptor_ReportsTheReplyOfTor()
    {
        using var keys = KeySet.Create();
        await using var proxy = new FakeSocksProxy();
        var dialer = new OnionDialer(proxy.Endpoint, keys.OnionAddress, NullLogger.Instance);

        var stream = await dialer.DialAsync(TestContext.Current.CancellationToken);

        Assert.Null(stream);
        Assert.Equal(SocksFailure.OnionDescriptorNotFound, dialer.LastFailure);
        Assert.Equal([keys.OnionAddress.Host], proxy.Hosts);
    }

    [Fact]
    public async Task DialAsync_RoutedOnionAddress_GivesAStreamToItsTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var keys = KeySet.Create();
        await using var proxy = new FakeSocksProxy();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        proxy.Route(keys.OnionAddress, OnionTarget.Loopback(((IPEndPoint)target.LocalEndpoint).Port));
        var dialer = new OnionDialer(proxy.Endpoint, keys.OnionAddress, NullLogger.Instance);

        await using var stream = await dialer.DialAsync(cancellationToken);
        using var accepted = await target.AcceptSocketAsync(cancellationToken);
        await stream!.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
        var received = new byte[3];
        await accepted.ReceiveAsync(received, SocketFlags.None, cancellationToken);

        Assert.Equal([1, 2, 3], received);
        Assert.Equal(SocksFailure.None, dialer.LastFailure);
    }

    [Fact]
    public async Task DialAsync_EachAttempt_UsesANewIsolationTokenSoTorBuildsANewCircuit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var keys = KeySet.Create();
        await using var proxy = new FakeSocksProxy();
        var dialer = new OnionDialer(proxy.Endpoint, keys.OnionAddress, NullLogger.Instance);

        await dialer.DialAsync(cancellationToken);
        await dialer.DialAsync(cancellationToken);

        Assert.Equal(2, proxy.Usernames.Count);
        Assert.NotEqual(proxy.Usernames[0], proxy.Usernames[1]);
    }

    [Fact]
    public async Task DialAsync_ProxyNeverAnswers_StopsWhenCancelled()
    {
        using var keys = KeySet.Create();
        await using var proxy = new FakeSocksProxy { Silent = true };
        var dialer = new OnionDialer(proxy.Endpoint, keys.OnionAddress, NullLogger.Instance);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await dialer.DialAsync(limit.Token));
    }
}
