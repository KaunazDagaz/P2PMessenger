using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Tests.Chats;

public sealed class ChatLogTests : IDisposable
{
    private readonly KeySet bob = KeySet.Create();
    private readonly KeySet carol = KeySet.Create();
    private readonly InMemorySeenMessageIds seen = new();
    private readonly InMemoryChatStore store;
    private readonly ManualTime time = new(DateTimeOffset.Parse("2026-11-16T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly ChatLog log;
    private readonly List<ChatChangedEventArgs> changes = [];

    public ChatLogTests()
    {
        store = new InMemoryChatStore(seen);
        log = new ChatLog(store, seen, time, []);
        log.Changed += (_, change) => changes.Add(change);
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task ReceiveAsync_NewText_IsStoredShownAndCountedAsUnread()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Text(1, "hello");

        var received = await log.ReceiveAsync(bob.IdentityKey, text, cancellationToken);

        Assert.True(received);
        var message = Assert.Single(log.MessagesWith(bob.IdentityKey));
        Assert.Equal(MessageDirection.Incoming, message.Direction);
        Assert.Equal(text.Id, message.Id);
        Assert.Equal("hello", message.Text);
        Assert.Equal(text.WrittenAt, message.WrittenAt);
        Assert.Equal(time.GetUtcNow(), message.RecordedAt);
        Assert.Equal(1, log.UnreadCount(bob.IdentityKey));
        Assert.Same(message, Assert.Single(await store.LoadAllAsync(cancellationToken)));
        Assert.True(await seen.ContainsAsync(text.Id, cancellationToken));
        var change = Assert.Single(changes);
        Assert.Equal(bob.IdentityKey, change.ContactKey);
        Assert.Same(message, change.Added);
        Assert.Empty(log.MessagesWith(carol.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ReceiveAsync_SameMessageIdAgain_IsDiscardedAndNeverShownTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Text(1, "hello");
        Assert.True(await log.ReceiveAsync(bob.IdentityKey, text, cancellationToken));

        var again = await log.ReceiveAsync(bob.IdentityKey, text, cancellationToken);
        var fromAnother = await log.ReceiveAsync(carol.IdentityKey, text, cancellationToken);

        Assert.False(again);
        Assert.False(fromAnother);
        Assert.Single(log.MessagesWith(bob.IdentityKey));
        Assert.Empty(log.MessagesWith(carol.IdentityKey));
        Assert.Single(changes);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task ReceiveAsync_OwnSentMessageReflectedBack_IsDiscarded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Text(log.NextSequence(bob.IdentityKey), "mine");
        await log.RecordOutgoingAsync(bob.IdentityKey, text, time.GetUtcNow(), cancellationToken);

        var received = await log.ReceiveAsync(bob.IdentityKey, text, cancellationToken);

        Assert.False(received);
        Assert.Equal(MessageDirection.Outgoing, Assert.Single(log.MessagesWith(bob.IdentityKey)).Direction);
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task ReceiveAsync_LaterTextFirst_ShowsEachSendersTextsInTheOrderWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await log.ReceiveAsync(bob.IdentityKey, Text(1, "one"), cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await log.ReceiveAsync(bob.IdentityKey, Text(4, "four"), cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await log.ReceiveAsync(bob.IdentityKey, Text(3, "three"), cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await log.ReceiveAsync(bob.IdentityKey, Text(2, "two"), cancellationToken);

        Assert.Equal(["one", "two", "three", "four"], log.MessagesWith(bob.IdentityKey).Select(static message => message.Text));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task RecordOutgoingAsync_BetweenIncomingTexts_KeepsTheOrderOnThisDevice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        DateTimeOffset beforeReply = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(1));
        await log.ReceiveAsync(bob.IdentityKey, Text(1, "reply"), cancellationToken);
        await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "question"), beforeReply, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "thanks"), time.GetUtcNow(), cancellationToken);

        var messages = log.MessagesWith(bob.IdentityKey);

        Assert.Equal(["question", "reply", "thanks"], messages.Select(static message => message.Text));
        Assert.Equal([MessageDirection.Outgoing, MessageDirection.Incoming, MessageDirection.Outgoing], messages.Select(static message => message.Direction));
        Assert.Equal(0, log.UnreadCount(carol.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void NextSequence_PerContact_StartsAtOneAndCountsUp()
    {
        Assert.Equal(1, log.NextSequence(bob.IdentityKey));
        Assert.Equal(2, log.NextSequence(bob.IdentityKey));
        Assert.Equal(1, log.NextSequence(carol.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Constructor_LoadedMessages_ContinueTheSequence()
    {
        var sent = ChatMessage.From(bob.IdentityKey, MessageDirection.Outgoing, Text(7, "old"), time.GetUtcNow());

        using var loaded = new ChatLog(store, seen, time, [sent]);

        Assert.Same(sent, Assert.Single(loaded.MessagesWith(bob.IdentityKey)));
        Assert.Equal(8, loaded.NextSequence(bob.IdentityKey));
    }

    [Fact]
    public async Task MarkRead_UnreadTexts_ResetsTheCountOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await log.ReceiveAsync(bob.IdentityKey, Text(1, "one"), cancellationToken);
        await log.ReceiveAsync(bob.IdentityKey, Text(2, "two"), cancellationToken);
        changes.Clear();

        log.MarkRead(bob.IdentityKey);
        log.MarkRead(bob.IdentityKey);
        log.MarkRead(carol.IdentityKey);

        Assert.Equal(0, log.UnreadCount(bob.IdentityKey));
        var change = Assert.Single(changes);
        Assert.Null(change.Added);
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public async Task RecordOutgoingAsync_NewText_IsWaitingAndItsIdIsSeen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Text(log.NextSequence(bob.IdentityKey), "hello");

        var recorded = await log.RecordOutgoingAsync(bob.IdentityKey, text, time.GetUtcNow(), cancellationToken);

        Assert.Equal(DeliveryState.Waiting, recorded.Delivery);
        Assert.Same(recorded, Assert.Single(log.WaitingFor(bob.IdentityKey)));
        Assert.Equal([bob.IdentityKey], log.ContactsWithWaitingMessages());
        Assert.True(await seen.ContainsAsync(text.Id, cancellationToken));
        Assert.Equal(DeliveryState.Waiting, Assert.Single(await store.LoadAllAsync(cancellationToken)).Delivery);
    }

    [Fact]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-07")]
    public async Task MarkReceivedAsync_AcknowledgedByTheContact_IsReceivedOnceAndStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Text(log.NextSequence(bob.IdentityKey), "hello");
        await log.RecordOutgoingAsync(bob.IdentityKey, text, time.GetUtcNow(), cancellationToken);
        changes.Clear();

        var first = await log.MarkReceivedAsync(bob.IdentityKey, text.Id, cancellationToken);
        var second = await log.MarkReceivedAsync(bob.IdentityKey, text.Id, cancellationToken);

        Assert.True(first);
        Assert.False(second);
        var message = Assert.Single(log.MessagesWith(bob.IdentityKey));
        Assert.Equal(DeliveryState.Received, message.Delivery);
        Assert.Equal(text.Id, message.Id);
        Assert.Empty(log.WaitingFor(bob.IdentityKey));
        Assert.Empty(log.ContactsWithWaitingMessages());
        Assert.Equal(DeliveryState.Received, Assert.Single(await store.LoadAllAsync(cancellationToken)).Delivery);
        var change = Assert.Single(changes);
        Assert.Null(change.Added);
        Assert.Same(message, change.Updated);
    }

    [Fact]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-07")]
    public async Task MarkReceivedAsync_FromAnotherContactOrForAnUnknownOrReceivedId_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var toBob = Text(log.NextSequence(bob.IdentityKey), "for Bob");
        await log.RecordOutgoingAsync(bob.IdentityKey, toBob, time.GetUtcNow(), cancellationToken);
        var fromBob = Text(1, "from Bob");
        await log.ReceiveAsync(bob.IdentityKey, fromBob, cancellationToken);
        changes.Clear();

        Assert.False(await log.MarkReceivedAsync(carol.IdentityKey, toBob.Id, cancellationToken));
        Assert.False(await log.MarkReceivedAsync(bob.IdentityKey, MessageId.Create(), cancellationToken));
        Assert.False(await log.MarkReceivedAsync(bob.IdentityKey, fromBob.Id, cancellationToken));

        Assert.Equal(DeliveryState.Waiting, Assert.Single(log.WaitingFor(bob.IdentityKey)).Delivery);
        Assert.Equal(
            [DeliveryState.Waiting, DeliveryState.None],
            log.MessagesWith(bob.IdentityKey).Select(static message => message.Delivery));
        Assert.Empty(changes);
    }

    [Fact]
    [Trait("Story", "US-15")]
    public async Task WaitingFor_SeveralWaitingTexts_AreInTheOrderWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = Text(log.NextSequence(bob.IdentityKey), "first");
        var second = Text(log.NextSequence(bob.IdentityKey), "second");
        var third = Text(log.NextSequence(bob.IdentityKey), "third");
        await log.RecordOutgoingAsync(bob.IdentityKey, third, time.GetUtcNow(), cancellationToken);
        await log.RecordOutgoingAsync(bob.IdentityKey, first, time.GetUtcNow(), cancellationToken);
        await log.RecordOutgoingAsync(bob.IdentityKey, second, time.GetUtcNow(), cancellationToken);
        Assert.True(await log.MarkReceivedAsync(bob.IdentityKey, second.Id, cancellationToken));

        var waiting = log.WaitingFor(bob.IdentityKey);

        Assert.Equal(["first", "third"], waiting.Select(static message => message.Text));
        Assert.Empty(log.WaitingFor(carol.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-15")]
    public void Constructor_LoadedWaitingAndReceivedTexts_KeepTheirStates()
    {
        var waiting = ChatMessage.From(bob.IdentityKey, MessageDirection.Outgoing, Text(1, "waiting"), time.GetUtcNow());
        var received = new ChatMessage(bob.IdentityKey, MessageDirection.Outgoing, MessageId.Create(), 2, time.GetUtcNow(), "received", time.GetUtcNow(), DeliveryState.Received);

        using var loaded = new ChatLog(store, seen, time, [waiting, received]);

        Assert.Same(waiting, Assert.Single(loaded.WaitingFor(bob.IdentityKey)));
        Assert.Equal([bob.IdentityKey], loaded.ContactsWithWaitingMessages());
    }

    [Theory]
    [InlineData(MessageDirection.Outgoing, DeliveryState.None)]
    [InlineData(MessageDirection.Incoming, DeliveryState.Waiting)]
    [InlineData(MessageDirection.Incoming, DeliveryState.Received)]
    public void ChatMessage_DeliveryThatDoesNotFitTheDirection_IsRefused(MessageDirection direction, DeliveryState delivery)
    {
        Assert.Throws<ArgumentException>(
            () => new ChatMessage(bob.IdentityKey, direction, MessageId.Create(), 1, time.GetUtcNow(), "text", time.GetUtcNow(), delivery));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    public async Task DeleteChatAsync_RemovesTheMessagesWaitingAndUnreadAndKeepsTheSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "one"), time.GetUtcNow(), cancellationToken);
        await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "two"), time.GetUtcNow(), cancellationToken);
        await log.ReceiveAsync(bob.IdentityKey, Text(1, "from Bob"), cancellationToken);
        await log.ReceiveAsync(carol.IdentityKey, Text(1, "from Carol"), cancellationToken);
        changes.Clear();

        var deleted = await log.DeleteChatAsync(bob.IdentityKey, cancellationToken);

        Assert.Equal(3, deleted);
        Assert.Empty(log.MessagesWith(bob.IdentityKey));
        Assert.Empty(log.WaitingFor(bob.IdentityKey));
        Assert.Empty(log.ContactsWithWaitingMessages());
        Assert.Equal(0, log.UnreadCount(bob.IdentityKey));
        Assert.Single(log.MessagesWith(carol.IdentityKey));
        Assert.Equal(3, log.NextSequence(bob.IdentityKey));
        var change = Assert.Single(changes);
        Assert.True(change.IsCleared);
        Assert.Equal(bob.IdentityKey, change.ContactKey);
        Assert.Equal("from Carol", Assert.Single(await store.LoadAllAsync(cancellationToken)).Text);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-11")]
    public async Task DeleteChatAsync_ThenStartedAgainFromTheStore_ContinuesTheSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var i = 0; i < 4; i++)
        {
            await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "text"), time.GetUtcNow(), cancellationToken);
        }

        await log.DeleteChatAsync(bob.IdentityKey, cancellationToken);
        using var restarted = new ChatLog(store, seen, time, await store.LoadAllAsync(cancellationToken), await store.LoadSentSequencesAsync(cancellationToken));

        Assert.Equal(5, restarted.NextSequence(bob.IdentityKey));
        Assert.Equal(1, restarted.NextSequence(carol.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void Constructor_SentSequenceAboveTheLoadedMessages_ContinuesAfterIt()
    {
        var sent = ChatMessage.From(bob.IdentityKey, MessageDirection.Outgoing, Text(2, "old"), time.GetUtcNow());

        using var loaded = new ChatLog(store, seen, time, [sent], new Dictionary<P2PMessenger.Core.Crypto.Ed25519PublicKey, long> { [bob.IdentityKey] = 9, [carol.IdentityKey] = 1 });

        Assert.Equal(10, loaded.NextSequence(bob.IdentityKey));
        Assert.Equal(2, loaded.NextSequence(carol.IdentityKey));
        Assert.Empty(loaded.MessagesWith(carol.IdentityKey));
        Assert.DoesNotContain(carol.IdentityKey, loaded.ContactsWithWaitingMessages());
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaitingAsync_OwnWaitingText_IsRemovedOnceAndItsNumberIsNotReused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "one"), time.GetUtcNow(), cancellationToken);
        var second = await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "two"), time.GetUtcNow(), cancellationToken);
        changes.Clear();

        Assert.True(await log.DeleteWaitingAsync(bob.IdentityKey, second.Id, cancellationToken));
        Assert.False(await log.DeleteWaitingAsync(bob.IdentityKey, second.Id, cancellationToken));

        Assert.Same(first, Assert.Single(log.MessagesWith(bob.IdentityKey)));
        Assert.Same(first, Assert.Single(log.WaitingFor(bob.IdentityKey)));
        Assert.False(log.IsWaiting(bob.IdentityKey, second.Id));
        Assert.True(log.IsWaiting(bob.IdentityKey, first.Id));
        Assert.Equal(3, log.NextSequence(bob.IdentityKey));
        Assert.Same(second, Assert.Single(changes).Removed);
        using var restarted = new ChatLog(store, seen, time, await store.LoadAllAsync(cancellationToken), await store.LoadSentSequencesAsync(cancellationToken));
        Assert.Equal(3, restarted.NextSequence(bob.IdentityKey));
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-07")]
    public async Task DeleteWaitingAsync_ReceivedIncomingOrAnotherContactsText_IsNotDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "one"), time.GetUtcNow(), cancellationToken);
        var incoming = Text(1, "from Bob");
        await log.ReceiveAsync(bob.IdentityKey, incoming, cancellationToken);

        Assert.False(await log.DeleteWaitingAsync(carol.IdentityKey, sent.Id, cancellationToken));
        Assert.False(await log.DeleteWaitingAsync(bob.IdentityKey, incoming.Id, cancellationToken));
        Assert.True(await log.MarkReceivedAsync(bob.IdentityKey, sent.Id, cancellationToken));
        Assert.False(await log.DeleteWaitingAsync(bob.IdentityKey, sent.Id, cancellationToken));

        Assert.Equal(2, log.MessagesWith(bob.IdentityKey).Count);
        Assert.Equal(2, (await store.LoadAllAsync(cancellationToken)).Count);
    }

    [Fact]
    [Trait("Story", "US-15")]
    public async Task EnterDeliveryAsync_WhileADeletionWaits_KeepsTheDeletionUntilTheTurnEnds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await log.RecordOutgoingAsync(bob.IdentityKey, Text(log.NextSequence(bob.IdentityKey), "one"), time.GetUtcNow(), cancellationToken);

        var turn = await log.EnterDeliveryAsync(bob.IdentityKey, cancellationToken);
        var deleting = log.DeleteWaitingAsync(bob.IdentityKey, sent.Id, cancellationToken);
        await Task.Delay(100, cancellationToken);

        Assert.False(deleting.IsCompleted);
        Assert.True(log.IsWaiting(bob.IdentityKey, sent.Id));
        turn.Dispose();
        turn.Dispose();
        Assert.True(await deleting);
        Assert.False(log.IsWaiting(bob.IdentityKey, sent.Id));
        using var again = await log.EnterDeliveryAsync(bob.IdentityKey, cancellationToken);
    }

    public void Dispose()
    {
        log.Dispose();
        bob.Dispose();
        carol.Dispose();
    }

    private static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddMinutes(sequence), text, out var message));
        return message;
    }
}
