using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Internet;
using P2PMessenger.Core;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Application;

public sealed record MessengerSetup
{
    public required IContactStore ContactStore { get; init; }

    public required IChatStore ChatStore { get; init; }

    public required ISeenMessageIds SeenMessageIds { get; init; }

    public IGroupStore? GroupStore { get; init; }

    public ConnectionOptions Connection { get; init; } = new();

    public DeliveryOptions Delivery { get; init; } = new();

    public IWakeUpScheduler? WakeUps { get; init; }

    public IPAddress ListenAddress { get; init; } = IPAddress.Any;

    public int? ListenPort { get; init; }

    public DiscoverySetup? Discovery { get; init; }

    public TorSetup? Tor { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public ILoggerFactory Loggers { get; init; } = NullLoggerFactory.Instance;

    public IFailureSink? Failures { get; init; }
    public static MessengerSetup InMemory()
    {
        InMemorySeenMessageIds seen = new();
        return new MessengerSetup
        {
            ContactStore = new InMemoryContactStore(),
            ChatStore = new InMemoryChatStore(seen),
            GroupStore = new InMemoryGroupStore(seen),
            SeenMessageIds = seen,
        };
    }
}
