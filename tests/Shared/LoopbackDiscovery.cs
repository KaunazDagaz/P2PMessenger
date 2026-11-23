using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Tests.Shared;

internal sealed class LoopbackDiscovery
{
    private readonly Lock gate = new();
    private readonly List<Link> links = [];
    private readonly List<byte[]> sent = [];

    public static DiscoveryOptions FastOptions { get; } = new()
    {
        AnnounceInterval = TimeSpan.FromMilliseconds(100),
        InterfaceRefreshInterval = TimeSpan.FromSeconds(1),
        HeardLifetime = TimeSpan.FromMilliseconds(1500),
        SweepAfter = TimeSpan.FromDays(1),
        SweepInterval = TimeSpan.FromDays(1),
        ScreeningsPerSecond = 10_000,
        ScreeningBurst = 10_000,
        ScreeningsPerSecondPerSource = 10_000,
        ScreeningBurstPerSource = 10_000,
    };

    public static LocalInterface Interface { get; } = new("hub0", 1, IPAddress.Parse("10.250.0.1"), 24, supportsMulticast: true);

    public IReadOnlyList<byte[]> Sent
    {
        get
        {
            lock (gate)
            {
                return [.. sent];
            }
        }
    }

    public Node CreateNode() => new(this);

    public IAsyncDisposable AnnounceAs(SessionContact recipientForSender, int port) => new Announcer(this, recipientForSender, port);

    public void Inject(byte[] datagram)
    {
        Link[] receivers;
        lock (gate)
        {
            receivers = [.. links];
        }

        foreach (Link receiver in receivers)
        {
            if (receiver.Node.InRange)
            {
                receiver.Enqueue([.. datagram]);
            }
        }
    }

    private Link Attach(Node node)
    {
        Link link = new(this, node);
        lock (gate)
        {
            links.Add(link);
        }

        return link;
    }

    private SocketError Deliver(Node from, ReadOnlyMemory<byte> datagram)
    {
        if (!from.InRange)
        {
            return SocketError.NetworkUnreachable;
        }

        Link[] receivers;
        lock (gate)
        {
            sent.Add(datagram.ToArray());
            receivers = [.. links];
        }

        foreach (Link receiver in receivers)
        {
            if (receiver.Node.InRange)
            {
                receiver.Enqueue(datagram.ToArray());
            }
        }

        return SocketError.Success;
    }

    private void Detach(Link link)
    {
        lock (gate)
        {
            links.Remove(link);
        }
    }

    internal sealed class Node
    {
        private readonly LoopbackDiscovery hub;
        private volatile bool inRange = true;
        private volatile NetworkSnapshot network = NetworkSnapshot.Create([Interface], hasOtherNetwork: false);

        public Node(LoopbackDiscovery hub)
        {
            this.hub = hub;
            Setup = new DiscoverySetup
            {
                Options = FastOptions,
                Interfaces = new NodeInterfaces(this),
                Links = _ => hub.Attach(this),
            };
        }

        public NetworkSnapshot Network
        {
            get => network;
            set => network = value;
        }

        public bool InRange
        {
            get => inRange;
            set => inRange = value;
        }

        public DiscoverySetup Setup { get; }

        public LoopbackDiscovery Hub => hub;
    }

    private sealed class NodeInterfaces : ILocalInterfaceSource
    {
        private readonly Node node;

        public NodeInterfaces(Node node)
        {
            this.node = node;
        }

        public NetworkSnapshot Read() => node.Network;
    }

    private sealed class Link : IDiscoveryLink
    {
        private readonly LoopbackDiscovery hub;
        private readonly Channel<byte[]> inbox = Channel.CreateUnbounded<byte[]>();

        public Link(LoopbackDiscovery hub, Node node)
        {
            this.hub = hub;
            Node = node;
        }

        public Node Node { get; }

        public SocketError Open() => SocketError.Success;

        public void UseInterfaces(IReadOnlyList<LocalInterface> interfaces)
        {
        }

        public void Enqueue(byte[] datagram) => inbox.Writer.TryWrite(datagram);

        public async ValueTask<DiscoveryDatagram> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
                byte[] datagram = await inbox.Reader.ReadAsync(cancellationToken);
                int length = Math.Min(datagram.Length, buffer.Length);
                datagram.AsMemory(0, length).CopyTo(buffer);
                return new DiscoveryDatagram(length, IPAddress.Loopback);
            }
            catch (ChannelClosedException)
            {
                return DiscoveryDatagram.Closed;
            }
        }

        public ValueTask<SocketError> SendAsync(LocalInterface from, IPEndPoint destination, ReadOnlyMemory<byte> datagram, bool sweep, CancellationToken cancellationToken) =>
            ValueTask.FromResult(hub.Deliver(Node, datagram));

        public ValueTask DisposeAsync()
        {
            hub.Detach(this);
            inbox.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Announcer : IAsyncDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly SessionContact recipient;
        private readonly Task loop;

        public Announcer(LoopbackDiscovery hub, SessionContact recipient, int port)
        {
            this.recipient = recipient;
            CancellationToken token = stop.Token;
            loop = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    hub.Inject(Announcement.Build([recipient], port));
                    try
                    {
                        await Task.Delay(50, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await loop;
            stop.Dispose();
            recipient.Dispose();
        }
    }
}
