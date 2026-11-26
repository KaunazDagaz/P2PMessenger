using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Copy;

namespace P2PMessenger.Transport.Tests.Copy;

public sealed class CopyChannelTests
{
    private static Ipv4Address Loopback
    {
        get
        {
            Assert.True(Ipv4Address.TryCreate(127, 0, 0, 1, out var address));
            return address;
        }
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-08")]
    public async Task ListenerAndDialer_OverLoopback_ExchangeEncryptedCopyMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = FastOptions();
        await using var listener = CopyListener.Start(new IPEndPoint(IPAddress.Loopback, 0), [Loopback], options, TimeProvider.System, NullLogger<CopyListener>.Instance);

        var acceptTask = listener.AcceptAsync(cancellationToken);
        var connectResult = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.True(connectResult.IsConnected);
        await using var client = connectResult.Channel;
        await using var server = await acceptTask;
        Assert.NotNull(server);

        Assert.True(CopyTransferStart.TryCreate(1, out var start));
        Assert.True(await client.SendAsync(start, cancellationToken));
        Assert.Equal(start, await server.ReceiveAsync(cancellationToken));

        using var owner = KeySet.Create();
        using var bob = KeySet.Create();
        var origin = CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey);
        Assert.True(await server.SendAsync(CopyConversationSaved.Create(origin), cancellationToken));
        Assert.Equal(CopyConversationSaved.Create(origin), await client.ReceiveAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    public async Task Listener_StrangerWithWrongSecretOrGarbage_IsRefusedSilentlyAndRealPhoneStillConnects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = FastOptions();
        await using var listener = CopyListener.Start(new IPEndPoint(IPAddress.Loopback, 0), [Loopback], options, TimeProvider.System, NullLogger<CopyListener>.Instance);
        var acceptTask = listener.AcceptAsync(cancellationToken);

        var wrongSecret = listener.PairingCode.PairingSecret.ToArray();
        wrongSecret[0] ^= 0xFF;
        var forgedCode = CopyPairingCode.Create(listener.PairingCode.ReceiverPublicKey, wrongSecret, listener.PairingCode.Port, [Loopback]);
        var forgedResult = await CopyDialer.ConnectAsync(forgedCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.False(forgedResult.IsConnected);
        Assert.Equal(CopyConnectFailure.Refused, forgedResult.Failure);

        using (var garbage = new TcpClient())
        {
            await garbage.ConnectAsync(listener.LocalEndpoint, cancellationToken);
            await garbage.GetStream().WriteAsync(new byte[64], cancellationToken);
        }

        Assert.False(acceptTask.IsCompleted);

        var legitimate = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.True(legitimate.IsConnected);
        await using var client = legitimate.Channel;
        await using var server = await acceptTask;
        Assert.NotNull(server);

        var secondCaller = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.False(secondCaller.IsConnected);
        Assert.Equal(CopyConnectFailure.Unreachable, secondCaller.Failure);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public async Task Dialer_WithMultipleAddressesIncludingUnreachableOnes_ConnectsToTheLiveOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = FastOptions() with { ConnectTimeout = TimeSpan.FromSeconds(3) };
        Assert.True(Ipv4Address.TryCreate(127, 0, 0, 2, out var unused));
        await using var listener = CopyListener.Start(new IPEndPoint(IPAddress.Loopback, 0), [Loopback, unused], options, TimeProvider.System, NullLogger<CopyListener>.Instance);

        var acceptTask = listener.AcceptAsync(cancellationToken);
        var connected = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.True(connected.IsConnected);
        await using var client = connected.Channel;
        await using var server = await acceptTask;
        Assert.NotNull(server);
    }

    [Fact]
    [Trait("Story", "US-22")]
    public async Task Channel_KeepAliveKeepsIdleSessionAliveWhileSilentPeerTimesOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new CopyOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            HandshakeTimeout = TimeSpan.FromSeconds(2),
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            IdleTimeout = TimeSpan.FromMilliseconds(600),
            WriteTimeout = TimeSpan.FromSeconds(2),
        };
        await using var listener = CopyListener.Start(new IPEndPoint(IPAddress.Loopback, 0), [Loopback], options, TimeProvider.System, NullLogger<CopyListener>.Instance);
        var acceptTask = listener.AcceptAsync(cancellationToken);
        var connected = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, NullLogger<CopyChannel>.Instance, cancellationToken);
        Assert.True(connected.IsConnected);
        await using var client = connected.Channel;
        await using var server = await acceptTask;
        Assert.NotNull(server);

        await Task.Delay(900, cancellationToken);

        Assert.True(CopyTransferStart.TryCreate(1, out var start));
        Assert.True(await client.SendAsync(start, cancellationToken));
        Assert.Equal(start, await server.ReceiveAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-10")]
    public async Task CopyLogs_HoldNoIpAddressesSecretsOrKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var capture = new MemoryLogger();
        var options = FastOptions();
        await using var listener = CopyListener.Start(
            new IPEndPoint(IPAddress.Loopback, 0),
            [Loopback],
            options,
            TimeProvider.System,
            capture.For<CopyListener>());

        var acceptTask = listener.AcceptAsync(cancellationToken);
        var connected = await CopyDialer.ConnectAsync(listener.PairingCode, options, TimeProvider.System, capture.For<CopyChannel>(), cancellationToken);
        Assert.True(connected.IsConnected);
        await using (connected.Channel)
        await using (await acceptTask)
        {
        }

        var secretHex = Convert.ToHexStringLower(listener.PairingCode.PairingSecret);
        var keyHex = Convert.ToHexStringLower(listener.PairingCode.ReceiverPublicKey.Bytes);
        var text = listener.PairingCode.ToText();
        var joined = string.Join("\n", capture.Lines);
        Assert.NotEmpty(joined);
        Assert.DoesNotContain("127.0.0.1", joined, StringComparison.Ordinal);
        Assert.DoesNotContain(secretHex, joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(keyHex, joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(text, joined, StringComparison.OrdinalIgnoreCase);
    }

    private static CopyOptions FastOptions() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(3),
        HandshakeTimeout = TimeSpan.FromSeconds(2),
        KeepAliveInterval = TimeSpan.FromSeconds(5),
        IdleTimeout = TimeSpan.FromSeconds(15),
        WriteTimeout = TimeSpan.FromSeconds(3),
    };

    private sealed class MemoryLogger
    {
        private readonly Lock gate = new();
        private readonly List<string> lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (gate)
                {
                    return [.. lines];
                }
            }
        }

        public ILogger<T> For<T>() => new TypedLogger<T>(this);

        private void Add(string line)
        {
            lock (gate)
            {
                lines.Add(line);
            }
        }

        private sealed class TypedLogger<T>(MemoryLogger owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Assert.Null(exception);
                owner.Add(formatter(state, null));
            }
        }
    }
}
