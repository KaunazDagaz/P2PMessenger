using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Copy;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Copy;

public sealed class PcCopyService
{
    private const int RandomPortAttempts = 32;

    private readonly OwnIdentity identity;
    private readonly ContactBook contacts;
    private readonly ChatLog chats;
    private readonly GroupBook groups;
    private readonly GroupChats groupChats;
    private readonly CopiedChatBook copiedChats;
    private readonly CopyOptions options;
    private readonly IPAddress listenAddress;
    private readonly int? listenPort;
    private readonly Func<IReadOnlyList<Ipv4Address>> readAddresses;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggers;
    private readonly IFailureSink? failures;

    internal PcCopyService(
        OwnIdentity identity,
        ContactBook contacts,
        ChatLog chats,
        GroupBook groups,
        GroupChats groupChats,
        CopiedChatBook copiedChats,
        CopyOptions options,
        IPAddress listenAddress,
        int? listenPort,
        Func<IReadOnlyList<Ipv4Address>> readAddresses,
        TimeProvider timeProvider,
        ILoggerFactory loggers,
        IFailureSink? failures)
    {
        this.identity = identity;
        this.contacts = contacts;
        this.chats = chats;
        this.groups = groups;
        this.groupChats = groupChats;
        this.copiedChats = copiedChats;
        this.options = options;
        this.listenAddress = listenAddress;
        this.listenPort = listenPort;
        this.readAddresses = readAddresses;
        this.timeProvider = timeProvider;
        this.loggers = loggers;
        this.failures = failures;
    }

    public IReadOnlyList<CopyCandidate> ListCandidates()
    {
        List<CopyCandidate> candidates = [];
        foreach (Group group in groups.All)
        {
            if (!IsCopyable(group))
            {
                continue;
            }

            CopyOriginId origin = CopyOriginId.ForGroup(identity.Keys.IdentityKey, group.Id);
            IReadOnlyList<GroupChatMessage> all = groupChats.MessagesIn(group.Id);
            int start = SelectGroupStartIndex(group, all);
            candidates.Add(new CopyCandidate(origin, group.Name, IsGroup: true, all.Count - start, all.Count));
        }

        foreach (Contact contact in contacts.All)
        {
            if (!contact.IsAdded)
            {
                continue;
            }

            CopyOriginId origin = CopyOriginId.ForContact(identity.Keys.IdentityKey, contact.IdentityKey);
            IReadOnlyList<ChatMessage> all = chats.MessagesWith(contact.IdentityKey);
            int start = SelectContactStartIndex(contact, all);
            candidates.Add(new CopyCandidate(origin, contact.Name, IsGroup: false, all.Count - start, all.Count));
        }

        return
        [
            .. candidates
                .OrderByDescending(static candidate => candidate.IsGroup)
                .ThenBy(static candidate => candidate.Title.Value, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static candidate => Convert.ToHexString(candidate.Origin.Bytes), StringComparer.Ordinal),
        ];
    }

    public static bool TryParsePairingCode(string? text, [NotNullWhen(true)] out CopyPairingCode? code, out CopyPairingCodeError error) =>
        CopyPairingCode.TryParseText(text, out code, out error);

    public Task<StartReceiveResult> StartReceivingAsync(CancellationToken cancellationToken) =>
        Task.Run(StartReceivingCore, cancellationToken);

    public async Task<PcCopyConnectResult> ConnectAsync(CopyPairingCode pairingCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pairingCode);

        CopyConnectResult result = await CopyDialer.ConnectAsync(
            pairingCode,
            options,
            timeProvider,
            loggers.CreateLogger<CopyChannel>(),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsConnected)
        {
            return PcCopyConnectResult.Failed(MapConnectFailure(result.Failure));
        }

        return PcCopyConnectResult.Ok(new PcCopySender(result.Channel, BuildSnapshots));
    }

    internal static IReadOnlyList<Ipv4Address> ReadAddresses(ILocalInterfaceSource source)
    {
        NetworkSnapshot snapshot = source.Read();
        List<Ipv4Address> result = [];
        Span<byte> bytes = stackalloc byte[Ipv4Address.Length];
        foreach (LocalInterface iface in snapshot.Interfaces)
        {
            if (iface.Address.TryWriteBytes(bytes, out int written)
                && written == Ipv4Address.Length
                && Ipv4Address.TryCreate(bytes, out Ipv4Address address))
            {
                result.Add(address);
            }
        }

        return [.. result.Distinct().OrderBy(static item => item).Take(CopyPairingCode.MaxAddresses)];
    }

    private static PcCopyConnectOutcome MapConnectFailure(CopyConnectFailure failure) => failure switch
    {
        CopyConnectFailure.TimedOut => PcCopyConnectOutcome.TimedOut,
        CopyConnectFailure.Refused => PcCopyConnectOutcome.Refused,
        CopyConnectFailure.Cancelled => PcCopyConnectOutcome.Cancelled,
        _ => PcCopyConnectOutcome.NotReachable,
    };

    private StartReceiveResult StartReceivingCore()
    {
        Ipv4Address[] addresses = [.. readAddresses().Distinct().OrderBy(static item => item).Take(CopyPairingCode.MaxAddresses)];
        if (addresses.Length == 0)
        {
            return StartReceiveResult.Failed(PcCopyReceiveProblem.NoLocalNetwork);
        }

        CopyListener? listener;
        if (listenPort is { } chosen)
        {
            if (chosen is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            {
                return StartReceiveResult.Failed(PcCopyReceiveProblem.ListenFailed);
            }

            try
            {
                listener = CopyListener.Start(
                    new IPEndPoint(listenAddress, chosen),
                    addresses,
                    options,
                    timeProvider,
                    loggers.CreateLogger<CopyListener>(),
                    failures);
            }
            catch (SocketException)
            {
                return StartReceiveResult.Failed(PcCopyReceiveProblem.ListenFailed);
            }
        }
        else
        {
            (listener, _) = ListenPorts.StartOnRandomPort(
                static () => RandomNumberGenerator.GetInt32(ConnectionService.RandomPortMin, ConnectionService.RandomPortMax + 1),
                port => CopyListener.Start(
                    new IPEndPoint(listenAddress, port),
                    addresses,
                    options,
                    timeProvider,
                    loggers.CreateLogger<CopyListener>(),
                    failures),
                RandomPortAttempts);
        }

        if (listener is null)
        {
            return StartReceiveResult.Failed(PcCopyReceiveProblem.ListenFailed);
        }

        return StartReceiveResult.Ok(new PcCopyReceiver(listener, copiedChats, options, timeProvider, loggers.CreateLogger<PcCopyReceiver>(), failures));
    }

    private List<CopySnapshot> BuildSnapshots(IReadOnlyList<CopyOriginId> selectedOrigins)
    {
        HashSet<CopyOriginId> wanted = [];
        List<CopyOriginId> ordered = [];
        foreach (CopyOriginId origin in selectedOrigins)
        {
            if (origin is not null && wanted.Add(origin))
            {
                ordered.Add(origin);
            }
        }

        if (ordered.Count == 0)
        {
            return [];
        }

        Dictionary<CopyOriginId, Contact> contactsByOrigin = [];
        foreach (Contact contact in contacts.All)
        {
            if (contact.IsAdded)
            {
                contactsByOrigin[CopyOriginId.ForContact(identity.Keys.IdentityKey, contact.IdentityKey)] = contact;
            }
        }

        Dictionary<CopyOriginId, Group> groupsByOrigin = [];
        foreach (Group group in groups.All)
        {
            if (IsCopyable(group))
            {
                groupsByOrigin[CopyOriginId.ForGroup(identity.Keys.IdentityKey, group.Id)] = group;
            }
        }

        List<CopySnapshot> snapshots = new(ordered.Count);
        foreach (CopyOriginId origin in ordered)
        {
            if (groupsByOrigin.TryGetValue(origin, out Group? group))
            {
                snapshots.Add(BuildGroupSnapshot(origin, group));
            }
            else if (contactsByOrigin.TryGetValue(origin, out Contact? contact))
            {
                snapshots.Add(BuildContactSnapshot(origin, contact));
            }
        }

        return snapshots;
    }

    private CopySnapshot BuildContactSnapshot(CopyOriginId origin, Contact contact)
    {
        IReadOnlyList<ChatMessage> all = chats.MessagesWith(contact.IdentityKey);
        int start = SelectContactStartIndex(contact, all);
        List<CopiedMessage> messages = new(all.Count - start);
        for (int index = start; index < all.Count; index++)
        {
            ChatMessage item = all[index];
            bool writtenByOwner = item.Direction == MessageDirection.Outgoing;
            messages.Add(new CopiedMessage(
                messages.Count + 1,
                writtenByOwner,
                writtenByOwner ? null : contact.Name,
                item.Text,
                item.WrittenAt));
        }

        return new CopySnapshot(origin, contact.Name, messages);
    }

    private CopySnapshot BuildGroupSnapshot(CopyOriginId origin, Group group)
    {
        IReadOnlyList<GroupChatMessage> all = groupChats.MessagesIn(group.Id);
        int start = SelectGroupStartIndex(group, all);
        List<CopiedMessage> messages = new(all.Count - start);
        for (int index = start; index < all.Count; index++)
        {
            GroupChatMessage item = all[index];
            bool writtenByOwner = item.AuthorKey.Equals(identity.Keys.IdentityKey);
            DisplayName? authorName = writtenByOwner ? null : ResolveGroupAuthorName(group, item);

            messages.Add(new CopiedMessage(
                messages.Count + 1,
                writtenByOwner,
                authorName,
                item.Text,
                item.WrittenAt));
        }

        return new CopySnapshot(origin, group.Name, messages);
    }

    private int SelectContactStartIndex(Contact contact, IReadOnlyList<ChatMessage> all) =>
        SelectNewestStartIndex(
            all,
            item => CopyConversationMessage.MeasureContentBytes(
                item.Direction == MessageDirection.Outgoing ? null : contact.Name,
                item.Text));

    private int SelectGroupStartIndex(Group group, IReadOnlyList<GroupChatMessage> all) =>
        SelectNewestStartIndex(
            all,
            item => CopyConversationMessage.MeasureContentBytes(
                item.AuthorKey.Equals(identity.Keys.IdentityKey) ? null : ResolveGroupAuthorName(group, item),
                item.Text));

    private bool IsCopyable(Group group) =>
        group.IsCreated || (group.State is GroupState.Left or GroupState.Removed or GroupState.Ended && groupChats.MessagesIn(group.Id).Count > 0);

    private DisplayName ResolveGroupAuthorName(Group group, GroupChatMessage item) =>
        group.FindAny(item.AuthorKey)?.Name
            ?? (contacts.TryGet(item.AuthorKey, out Contact? contact) ? contact.Name : group.Name);

    private int SelectNewestStartIndex<T>(IReadOnlyList<T> all, Func<T, int> measureBytes)
    {
        int start = all.Count;
        int taken = 0;
        long totalBytes = 0;
        for (int index = all.Count - 1; index >= 0 && taken < options.MaxMessagesPerConversation; index--)
        {
            int messageBytes = measureBytes(all[index]);
            if (totalBytes + messageBytes > options.MaxConversationBytes)
            {
                break;
            }

            totalBytes += messageBytes;
            taken++;
            start = index;
        }

        return start;
    }
}
