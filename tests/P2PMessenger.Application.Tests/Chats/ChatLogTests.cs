using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Tests.Chats;

public sealed class ChatLogTests : IDisposable
{
    private readonly KeySet bob = KeySet.Create();
    private readonly KeySet carol = KeySet.Create();
    private readonly InMemoryChatStore store = new();
    private readonly InMemorySeenMessageIds seen = new();
    private readonly ManualTime time = new(DateTimeOffset.Parse("2026-11-16T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly ChatLog log;
    private readonly List<ChatChangedEventArgs> changes = [];

    public ChatLogTests()
    {
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
