using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

public sealed class TorControlClientTests : IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-tor-control-").FullName;
    private readonly ConcurrentQueue<ControlReply> events = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task Authenticate_CookieFileThatTorNames_SendsTheCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await using var client = await ConnectAsync(tor);

        await client.AuthenticateAsync(tor.CookieFile, cancellationToken);

        Assert.Contains("PROTOCOLINFO 1", tor.Commands);
        Assert.Contains("AUTHENTICATE " + Convert.ToHexString(tor.Cookie), tor.Commands);
    }

    [Fact]
    public async Task Authenticate_TorNamesAnotherCookieFile_RefusesWithoutSendingTheCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.NamedCookieFile = Path.Combine(Path.GetTempPath(), "another", "control_auth_cookie"));
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.AuthenticateAsync(tor.CookieFile, cancellationToken));

        Assert.DoesNotContain(tor.Commands, static command => command.StartsWith("AUTHENTICATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authenticate_NoCookieMethod_Refuses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.Methods = "NULL,HASHEDPASSWORD");
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.AuthenticateAsync(tor.CookieFile, cancellationToken));

        Assert.DoesNotContain(tor.Commands, static command => command.StartsWith("AUTHENTICATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task Authenticate_CookieOfTheWrongLength_Refuses(int length)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await File.WriteAllBytesAsync(tor.CookieFile, new byte[length], cancellationToken);
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.AuthenticateAsync(tor.CookieFile, cancellationToken));
    }

    [Fact]
    public async Task Authenticate_WrongCookie_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await File.WriteAllBytesAsync(tor.CookieFile, RandomNumberGenerator.GetBytes(TorControlClient.CookieLength), cancellationToken);
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.AuthenticateAsync(tor.CookieFile, cancellationToken));
    }

    [Fact]
    public async Task AddOnion_UnixSocketTarget_SendsTheExpandedKeyAndTheTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string serviceId = "hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd";
        await using var tor = Start(static fake => fake.ServiceId = serviceId);
        await using var client = await ConnectAsync(tor);
        var key = RandomNumberGenerator.GetBytes(OnionServiceKey.ExpandedLength);

        var result = await client.AddOnionAsync(key, 45821, OnionTarget.UnixSocket("/run/p2pm/tor/onion.sock"), cancellationToken);

        Assert.Equal(serviceId, result);
        Assert.Contains("ADD_ONION ED25519-V3:" + Convert.ToBase64String(key) + " Port=45821,unix:/run/p2pm/tor/onion.sock", tor.Commands);
    }

    [Fact]
    public async Task AddOnion_LoopbackTarget_SendsTheLoopbackPort()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.ServiceId = "hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd");
        await using var client = await ConnectAsync(tor);

        await client.AddOnionAsync(new byte[OnionServiceKey.ExpandedLength], 45821, OnionTarget.Loopback(50123), cancellationToken);

        Assert.Contains(tor.Commands, static command => command.EndsWith(" Port=45821,127.0.0.1:50123", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tooshort")]
    [InlineData("HVABPQ7IIOEVVEVXBKTU2G36XSOJQLGPF3CJNDGAZVK7CKXUMYGCMYYD")]
    public async Task AddOnion_NoValidServiceId_Throws(string? serviceId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(fake => fake.ServiceId = serviceId);
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.AddOnionAsync(new byte[OnionServiceKey.ExpandedLength], 45821, OnionTarget.Loopback(50123), cancellationToken));
    }

    [Fact]
    public async Task AddOnion_KeyOfTheWrongLength_IsNotSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddOnionAsync(new byte[32], 45821, OnionTarget.Loopback(50123), cancellationToken));

        Assert.Empty(tor.Commands);
    }

    [Fact]
    public async Task RefusedCommand_ThrowsAndTheNextCommandStillWorks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.RefuseCommand = "TAKEOWNERSHIP");
        await using var client = await ConnectAsync(tor);

        await Assert.ThrowsAsync<TorControlException>(() => client.TakeOwnershipAsync(cancellationToken));

        Assert.Equal("NOTICE BOOTSTRAP PROGRESS=0 TAG=starting SUMMARY=\"Starting\"", await client.GetInfoAsync("status/bootstrap-phase", cancellationToken));
    }

    [Fact]
    public async Task Events_BetweenReplies_GoToTheEventHandler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await using var client = await ConnectAsync(tor);
        await client.SetEventsAsync(TorEvents.Subscribed, cancellationToken);

        await tor.SendAsync("650 STATUS_CLIENT NOTICE CIRCUIT_ESTABLISHED");
        await client.TakeOwnershipAsync(cancellationToken);

        Assert.True(await Eventually.TrueAsync(() => !events.IsEmpty, cancellationToken));
        Assert.Equal("STATUS_CLIENT NOTICE CIRCUIT_ESTABLISHED", Assert.Single(events).FirstLine);
        Assert.Contains("SETEVENTS STATUS_CLIENT STATUS_GENERAL HS_DESC NETWORK_LIVENESS", tor.Commands);
    }

    [Fact]
    public async Task SilentTor_CommandTimesOutAndTheConnectionIsNotUsedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.Silent = true);
        await using var client = TorControlClient.Over(await ConnectStreamAsync(tor), events.Enqueue, TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TorControlException>(() => client.TakeOwnershipAsync(cancellationToken));
        await Assert.ThrowsAsync<TorControlException>(() => client.TakeOwnershipAsync(cancellationToken));

        Assert.Single(tor.Commands);
    }

    [Fact]
    public async Task TorCloses_PendingCommandFailsAndClosedCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start(static fake => fake.Silent = true);
        await using var client = await ConnectAsync(tor);

        var pending = client.TakeOwnershipAsync(cancellationToken);
        await Eventually.TrueAsync(() => tor.Commands.Count == 1, cancellationToken);
        tor.Exit();

        await Assert.ThrowsAsync<TorControlException>(() => pending);
        await client.Closed.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task ReplyThatNoCommandAskedFor_ClosesTheConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await using var client = await ConnectAsync(tor);

        await tor.SendAsync("250 OK");

        await client.Closed.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await Assert.ThrowsAsync<TorControlException>(() => client.TakeOwnershipAsync(cancellationToken));
    }

    [Fact]
    public async Task ConnectAsync_AddressThatIsNotLoopback_Refuses()
    {
        await Assert.ThrowsAsync<TorControlException>(() => TorControlClient.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9051), events.Enqueue, CommandTimeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shutdown_SendsTheSignal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var tor = Start();
        await using var client = await ConnectAsync(tor);

        await client.ShutdownAsync(cancellationToken);

        Assert.Contains("SIGNAL SHUTDOWN", tor.Commands);
        await tor.Exited.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    private FakeTor Start(Action<FakeTor>? configure = null)
    {
        var cookie = RandomNumberGenerator.GetBytes(TorControlClient.CookieLength);
        var cookieFile = Path.Combine(folder, "control_auth_cookie");
        File.WriteAllBytes(cookieFile, cookie);
        var tor = new FakeTor(cookieFile, cookie);
        configure?.Invoke(tor);
        tor.Serve();
        return tor;
    }

    private Task<TorControlClient> ConnectAsync(FakeTor tor) =>
        TorControlClient.ConnectAsync(tor.Endpoint, events.Enqueue, CommandTimeout, TestContext.Current.CancellationToken);

    private static async Task<Stream> ConnectStreamAsync(FakeTor tor)
    {
        var socket = new System.Net.Sockets.TcpClient();
        await socket.ConnectAsync(tor.Endpoint, TestContext.Current.CancellationToken);
        return socket.GetStream();
    }
}
