using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tcp;
using P2PMessenger.Transport.Tor;
using static P2PMessenger.Integration.Tests.Connections.TestContacts;

namespace P2PMessenger.Integration.Tests.Connections;

[Trait("Story", "US-09")]
public sealed class OnionListenerTests : IDisposable
{
    private static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(5);

    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-onion-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact(Timeout = 30_000)]
    public async Task Contact_ThroughTheUnixSocket_GetsASession()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "tor on Windows has no Unix sockets.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        var aliceForBob = bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, SocketPath(), NullLogger<OnionListener>.Instance);

        var result = await alice.Manager.ConnectAsync(bobForAlice, new UnixDialer(onion.Target.SocketPath!), cancellationToken);

        Assert.Equal(OnionTargetKind.UnixSocket, onion.Kind);
        Assert.True(result.IsConnected);
        var accepted = await bob.Observer.NextConnectedAsync(cancellationToken);
        Assert.Same(aliceForBob, accepted.Contact);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(onion.Target.SocketPath)!));
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Contact_ThroughTheLoopbackPort_GetsASession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, null, NullLogger<OnionListener>.Instance);

        var result = await alice.Manager.ConnectAsync(bobForAlice, new TcpDialer(new IPEndPoint(IPAddress.Loopback, onion.Target.Port)), cancellationToken);

        Assert.Equal(OnionTargetKind.LoopbackPort, onion.Kind);
        Assert.False(onion.Target.IsUnixSocket);
        Assert.True(result.IsConnected);
        await bob.Observer.NextConnectedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("quote\"d")]
    [InlineData("relative")]
    public async Task UnusableSocketPath_UsesALoopbackPortInstead(string name)
    {
        await using var bob = TestPeer.Start();
        var path = name == "relative" ? "onion.sock" : Path.Combine(folder, name, "onion.sock");

        await using var onion = OnionListener.Start(bob.Manager, path, NullLogger<OnionListener>.Instance);

        Assert.Equal(OnionTargetKind.LoopbackPort, onion.Kind);
        Assert.InRange(onion.Target.Port, 1, 65535);
    }

    [Fact(Timeout = 30_000)]
    [Trait("NFR", "NFR-09")]
    public async Task Stranger_ThroughTheOnionListener_IsClosedWithoutAByteSentBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bob = TestPeer.Start();
        await using var onion = OnionListener.Start(bob.Manager, SocketPath(), NullLogger<OnionListener>.Instance);
        using var stranger = KeySet.Create();
        using var bobForStranger = Create(stranger, bob.Keys);
        using var initiator = SessionInitiator.Start(stranger, bobForStranger);
        await using var stream = await new OnionDialer(onion.Target).DialAsync(cancellationToken) ?? throw new InvalidOperationException("No connection.");

        await stream.WriteAsync(initiator.FirstFlight.ToArray(), cancellationToken);

        Assert.Equal(0, await CountBytesUntilClosedAsync(stream, CloseLimit, cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    [Fact(Timeout = 60_000)]
    [Trait("NFR", "NFR-09")]
    public async Task OnionConnections_HaveNoAddress_SoOnlyTheOverallLimitApplies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TestOptions.Fast with
        {
            HintTimeout = TimeSpan.FromSeconds(20),
            HandshakeTimeout = TimeSpan.FromSeconds(20),
            Admission = TestOptions.Generous with { MaxPendingPerSource = 1, MaxPendingConnections = 3 },
        };
        await using var bob = TestPeer.Start(options);
        await using var onion = OnionListener.Start(bob.Manager, SocketPath(), NullLogger<OnionListener>.Instance);
        var dialer = new OnionDialer(onion.Target);
        List<Stream> open = [];
        try
        {
            for (var index = 0; index < 3; index++)
            {
                open.Add(await dialer.DialAsync(cancellationToken) ?? throw new InvalidOperationException("No connection."));
            }

            await Task.Delay(300, cancellationToken);
            await using (var fourth = await dialer.DialAsync(cancellationToken) ?? throw new InvalidOperationException("No connection."))
            {
                Assert.Equal(0, await CountBytesUntilClosedAsync(fourth, CloseLimit, cancellationToken));
            }

            foreach (var stream in open)
            {
                Assert.Equal(Raw.StillOpen, await CountBytesUntilClosedAsync(stream, TimeSpan.FromMilliseconds(200), cancellationToken));
            }
        }
        finally
        {
            foreach (var stream in open)
            {
                await stream.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Dispose_RemovesTheSocketFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "tor on Windows has no Unix sockets.");
        await using var bob = TestPeer.Start();
        var path = SocketPath();
        var onion = OnionListener.Start(bob.Manager, path, NullLogger<OnionListener>.Instance);
        Assert.True(File.Exists(path));

        await onion.DisposeAsync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Start_StaleSocketFileFromAnEarlierRun_IsReplaced()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "tor on Windows has no Unix sockets.");
        await using var bob = TestPeer.Start();
        var path = SocketPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "stale", TestContext.Current.CancellationToken);

        await using var onion = OnionListener.Start(bob.Manager, path, NullLogger<OnionListener>.Instance);

        Assert.Equal(OnionTargetKind.UnixSocket, onion.Kind);
    }

    private static async Task<int> CountBytesUntilClosedAsync(Stream stream, TimeSpan limit, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        var buffer = new byte[1024];
        var total = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    return total;
                }

                total += read;
            }
        }
        catch (IOException)
        {
            return total;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Raw.StillOpen;
        }
    }

    private string SocketPath() => Path.Combine(folder, "tor", "onion.sock");

    private sealed class UnixDialer(string path) : IDialer
    {
        public async ValueTask<Stream?> DialAsync(CancellationToken cancellationToken)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private sealed class OnionDialer(OnionTarget target) : IDialer
    {
        public ValueTask<Stream?> DialAsync(CancellationToken cancellationToken) =>
            target.SocketPath is { } path
                ? new UnixDialer(path).DialAsync(cancellationToken)
                : new TcpDialer(new IPEndPoint(IPAddress.Loopback, target.Port)).DialAsync(cancellationToken);
    }
}
