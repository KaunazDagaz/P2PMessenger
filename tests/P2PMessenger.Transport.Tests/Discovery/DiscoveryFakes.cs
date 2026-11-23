using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

internal sealed record SentDatagram(LocalInterface From, IPEndPoint Destination, byte[] Datagram, bool Sweep);

internal sealed class FakeLink : IDiscoveryLink
{
    private readonly Lock gate = new();
    private readonly List<SentDatagram> sent = [];
    private readonly Channel<(byte[] Datagram, IPAddress Source)> inbox = Channel.CreateUnbounded<(byte[], IPAddress)>();

    public SocketError OpenResult { get; set; } = SocketError.Success;

    public SocketError SendResult { get; set; } = SocketError.Success;

    public int Opened { get; private set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<LocalInterface> Interfaces { get; private set; } = [];

    public IReadOnlyList<SentDatagram> Sent
    {
        get
        {
            lock (gate)
            {
                return [.. sent];
            }
        }
    }

    public SocketError Open()
    {
        Opened++;
        return OpenResult;
    }

    public void UseInterfaces(IReadOnlyList<LocalInterface> interfaces) => Interfaces = interfaces;

    public void Deliver(byte[] datagram, IPAddress source) => inbox.Writer.TryWrite((datagram, source));

    public async ValueTask<DiscoveryDatagram> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            var (datagram, source) = await inbox.Reader.ReadAsync(cancellationToken);
            datagram.AsMemory(0, Math.Min(datagram.Length, buffer.Length)).CopyTo(buffer);
            return new DiscoveryDatagram(datagram.Length, source);
        }
        catch (ChannelClosedException)
        {
            return DiscoveryDatagram.Closed;
        }
    }

    public ValueTask<SocketError> SendAsync(LocalInterface from, IPEndPoint destination, ReadOnlyMemory<byte> datagram, bool sweep, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            sent.Add(new SentDatagram(from, destination, datagram.ToArray(), sweep));
        }

        return ValueTask.FromResult(SendResult);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeInterfaces : ILocalInterfaceSource
{
    public NetworkSnapshot Snapshot { get; set; } = NetworkSnapshot.None;

    public Exception? Failure { get; set; }

    public int Reads { get; private set; }

    public NetworkSnapshot Read()
    {
        Reads++;
        return Failure is { } failure ? throw failure : Snapshot;
    }
}

internal sealed class FakeContacts : IKnownContacts
{
    public IReadOnlyList<SessionContact> Current { get; set; } = [];
}

internal sealed class RecordingDiscoveryObserver : IDiscoveryObserver
{
    private readonly Lock gate = new();
    private readonly List<(SessionContact Contact, IPEndPoint Endpoint)> heard = [];
    private readonly List<NetworkSnapshot> networks = [];

    public IReadOnlyList<(SessionContact Contact, IPEndPoint Endpoint)> HeardContacts
    {
        get
        {
            lock (gate)
            {
                return [.. heard];
            }
        }
    }

    public IReadOnlyList<NetworkSnapshot> Networks
    {
        get
        {
            lock (gate)
            {
                return [.. networks];
            }
        }
    }

    public void Heard(SessionContact contact, IPEndPoint endpoint)
    {
        lock (gate)
        {
            heard.Add((contact, endpoint));
        }
    }

    public void NetworkChanged(NetworkSnapshot network)
    {
        lock (gate)
        {
            networks.Add(network);
        }
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly Lock gate = new();
    private readonly List<(EventId Id, string Message)> entries = [];

    public IReadOnlyList<(EventId Id, string Message)> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (gate)
        {
            entries.Add((eventId, formatter(state, exception)));
        }
    }
}

internal sealed class DiscoveryPeers : IDisposable
{
    private readonly List<IDisposable> owned = [];

    public DiscoveryPeers(int contactCount)
    {
        Own = KeySet.Create();
        owned.Add(Own);
        List<SessionContact> forOwn = [];
        List<SessionContact> forOthers = [];
        for (var index = 0; index < contactCount; index++)
        {
            var other = KeySet.Create();
            owned.Add(other);
            Assert.True(SessionContact.TryCreate(Own, other.IdentityKey, other.StaticKey, out var contact));
            Assert.True(SessionContact.TryCreate(other, Own.IdentityKey, Own.StaticKey, out var reverse));
            owned.Add(contact);
            owned.Add(reverse);
            forOwn.Add(contact);
            forOthers.Add(reverse);
        }

        ContactsOfOwn = forOwn;
        OwnForEachContact = forOthers;
    }

    public KeySet Own { get; }

    public IReadOnlyList<SessionContact> ContactsOfOwn { get; }

    public IReadOnlyList<SessionContact> OwnForEachContact { get; }

    public void Dispose()
    {
        foreach (var item in owned)
        {
            item.Dispose();
        }
    }
}
