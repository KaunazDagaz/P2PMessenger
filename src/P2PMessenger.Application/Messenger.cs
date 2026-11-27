using System.Text;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.Internet;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application;

public sealed class Messenger : IAsyncDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly GroupPassOn groupPassOn;
    private readonly Outbox outbox;
    private int disposed;

    private Messenger(
        OwnIdentity identity,
        ContactBook contacts,
        ChatLog chats,
        GroupBook groups,
        GroupChats groupChats,
        CopiedChatBook copiedChats,
        PcCopyService pcCopy,
        GroupPassOn groupPassOn,
        ConnectionService connections,
        Outbox outbox,
        TimeProvider timeProvider)
    {
        Identity = identity;
        Contacts = contacts;
        Chats = chats;
        Groups = groups;
        GroupChats = groupChats;
        CopiedChats = copiedChats;
        PcCopy = pcCopy;
        Connections = connections;
        InternetRoute = InternetRoute.Off(connections);
        this.groupPassOn = groupPassOn;
        this.outbox = outbox;
        this.timeProvider = timeProvider;
    }

    public OwnIdentity Identity { get; }

    public ContactBook Contacts { get; }

    public ChatLog Chats { get; }

    public GroupBook Groups { get; }

    public GroupChats GroupChats { get; }

    public CopiedChatBook CopiedChats { get; }

    public PcCopyService PcCopy { get; }

    public ConnectionService Connections { get; }

    public InternetRoute InternetRoute { get; private set; }

    public static async Task<Messenger> StartAsync(OwnIdentity identity, MessengerSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(setup);
        string? problem = setup.Delivery.FindProblem() ?? setup.Copy.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(setup));
        }

        IReadOnlyList<Contact> contacts = await setup.ContactStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ChatMessage> messages = await setup.ChatStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Ed25519PublicKey, long> sentSequences = await setup.ChatStore.LoadSentSequencesAsync(cancellationToken).ConfigureAwait(false);

        IGroupStore groupStore = setup.GroupStore ?? new InMemoryGroupStore(setup.SeenMessageIds);
        IReadOnlyList<LoadedGroup> loadedGroups = await groupStore.LoadGroupsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupChatMessage> groupMessages;
        IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>> deletedGroupMessages;
        IReadOnlyList<GroupAcknowledgement> groupAcknowledgements;
        IReadOnlyList<CopiedConversation> loadedCopiedChats;
        ICopiedChatStore copiedChatStore = setup.CopiedChatStore ?? new InMemoryCopiedChatStore();
        try
        {
            groupMessages = await groupStore.LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            deletedGroupMessages = await groupStore.LoadDeletedAsync(cancellationToken).ConfigureAwait(false);
            groupAcknowledgements = await groupStore.LoadAcknowledgementsAsync(cancellationToken).ConfigureAwait(false);
            loadedCopiedChats = await copiedChatStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (LoadedGroup group in loadedGroups)
            {
                group.Key.Dispose();
            }

            throw;
        }

        ContactBook contactBook = new(identity, setup.ContactStore, contacts);
        ChatLog chatLog = new(setup.ChatStore, setup.SeenMessageIds, setup.TimeProvider, messages, sentSequences);
        GroupBook groupBook = new(identity, contactBook, groupStore, loadedGroups, setup.Loggers.CreateLogger<GroupBook>(), setup.Failures);
        GroupChats groupChats = new(groupBook, identity, groupStore, setup.SeenMessageIds, setup.TimeProvider, groupMessages, deletedGroupMessages, groupAcknowledgements, chatLog.EnterDeliveryAsync);
        CopiedChatBook copiedChatBook = new(copiedChatStore, loadedCopiedChats);
        ILocalInterfaceSource interfaceSource = setup.Discovery?.Interfaces ?? new SystemInterfaceSource();
        Func<IReadOnlyList<Ipv4Address>> readCopyAddresses = setup.CopyAddressSource ?? (() => PcCopyService.ReadAddresses(interfaceSource));
        PcCopyService pcCopy = new(
            identity,
            contactBook,
            chatLog,
            groupBook,
            groupChats,
            copiedChatBook,
            setup.Copy,
            setup.CopyListenAddress ?? setup.ListenAddress,
            setup.CopyListenPort,
            readCopyAddresses,
            setup.TimeProvider,
            setup.Loggers,
            setup.Failures);
        GroupPassOn groupPassOn = new(groupBook, groupChats, setup.Delivery, setup.TimeProvider);
        ConnectionService connections = new(identity, contactBook, chatLog, groupBook, groupChats, groupPassOn, setup.Connection, setup.TimeProvider, setup.Loggers, setup.Failures);
        IWakeUpScheduler scheduler = setup.WakeUps ?? new TimerWakeUpScheduler(setup.TimeProvider);
        Outbox outbox = new(chatLog, groupBook, groupChats, groupPassOn, connections, scheduler, setup.Delivery, setup.TimeProvider, setup.Loggers.CreateLogger<Outbox>(), setup.Failures);
        Messenger messenger = new(identity, contactBook, chatLog, groupBook, groupChats, copiedChatBook, pcCopy, groupPassOn, connections, outbox, setup.TimeProvider);
        try
        {
            await groupBook.JoinWhereReadyAsync(cancellationToken).ConfigureAwait(false);
            await connections.ListenAsync(setup.ListenAddress, setup.ListenPort, cancellationToken).ConfigureAwait(false);
            if (setup.Discovery is { } discovery)
            {
                connections.StartDiscovery(discovery);
            }

            if (setup.Tor is { } tor)
            {
                messenger.InternetRoute = await InternetRoute.StartAsync(tor, identity.Keys, connections, setup.Loggers, setup.TimeProvider).ConfigureAwait(false);
            }
        }
        catch
        {
            await messenger.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return messenger;
    }

    public async Task<SendResult> SendAsync(Ed25519PublicKey contactKey, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new SendResult(SendOutcome.Empty, null);
        }

        if (!TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, text, out _))
        {
            bool tooLong = Encoding.UTF8.GetByteCount(text) > TextMessage.MaxUtf8Length;
            return new SendResult(tooLong ? SendOutcome.TooLong : SendOutcome.NotSendable, null);
        }

        if (!Contacts.TryGet(contactKey, out Contact? contact))
        {
            return new SendResult(SendOutcome.NotAContact, null);
        }

        if (!contact.IsAdded)
        {
            return new SendResult(SendOutcome.WaitingForFirstConnection, null);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        long sequence = Chats.NextSequence(contactKey);
        if (!TextMessage.TryCreate(MessageId.Create(), sequence, now, text, out TextMessage? message))
        {
            return new SendResult(SendOutcome.NotSendable, null);
        }

        ChatMessage recorded = await Chats.RecordOutgoingAsync(contactKey, message, now, cancellationToken).ConfigureAwait(false);
        outbox.Request(contactKey);
        return new SendResult(SendOutcome.Queued, recorded);
    }

    public Task<CreateGroupResult> CreateGroupAsync(string? name, IReadOnlyCollection<Ed25519PublicKey> contactKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKeys);

        return Groups.CreateAsync(name, contactKeys, cancellationToken);
    }

    public Task<RemoveMemberResult> RemoveGroupMemberAsync(GroupId groupId, Ed25519PublicKey memberKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(memberKey);

        return Groups.RemoveMemberAsync(groupId, memberKey, cancellationToken);
    }

    public Task<LeaveGroupOutcome> LeaveGroupAsync(GroupId groupId, Ed25519PublicKey? successorKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        return Groups.LeaveAsync(groupId, successorKey, cancellationToken);
    }

    public Task<GroupSendResult> SendToGroupAsync(GroupId groupId, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        return GroupChats.SendAsync(groupId, text, cancellationToken);
    }

    public Task<int> DeleteChatAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        return Chats.DeleteChatAsync(contactKey, cancellationToken);
    }

    public Task<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        return Chats.DeleteWaitingAsync(contactKey, id, cancellationToken);
    }

    public Task<int> DeleteGroupChatAsync(GroupId groupId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        return GroupChats.DeleteChatAsync(groupId, cancellationToken);
    }

    public Task<bool> DeleteWaitingGroupMessageAsync(GroupId groupId, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(id);

        return GroupChats.DeleteWaitingAsync(groupId, id, cancellationToken);
    }

    public Task<bool> DeleteCopiedChatAsync(CopyOriginId origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return CopiedChats.DeleteAsync(origin, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await InternetRoute.DisposeAsync().ConfigureAwait(false);
        await outbox.DisposeAsync().ConfigureAwait(false);
        await Connections.DisposeAsync().ConfigureAwait(false);
        groupPassOn.Dispose();
        CopiedChats.Dispose();
        GroupChats.Dispose();
        Groups.Dispose();
        Chats.Dispose();
        Contacts.Dispose();
    }
}
