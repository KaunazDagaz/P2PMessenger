using P2PMessenger.Application.Background;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Background;

public sealed class MessageAlertsTests
{
    [Fact]
    [Trait("Story", "US-19")]
    public async Task IncomingMessage_AppInTheBackground_ShowsWhoSentItWithoutText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        using var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => false);

        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "secret plan"), cancellationToken));

        var notice = Assert.Single(notifier.Shown);
        Assert.Equal("Bob", notice.SenderName);
        Assert.Equal(bob.IdentityKey, notice.ContactKey);
        Assert.Null(notice.Preview);
        Assert.DoesNotContain("secret", notice.ToString(), StringComparison.Ordinal);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task IncomingMessage_PreviewsTurnedOn_ShowsTheText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        var settings = new BackgroundSettings(new MemorySettings()) { ShowPreviews = true };
        using var alerts = new MessageAlerts(messenger, notifier, settings, () => false);

        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "see you at noon"), cancellationToken));

        Assert.Equal("see you at noon", Assert.Single(notifier.Shown).Preview);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task IncomingMessage_AppVisible_ShowsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        using var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => true);

        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "hello"), cancellationToken));

        Assert.Empty(notifier.Shown);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task IncomingMessage_NotificationsNotAllowed_ShowsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier { IsAllowed = false };
        using var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => false);

        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "hello"), cancellationToken));

        Assert.Empty(notifier.Shown);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task OwnMessage_AppInTheBackground_ShowsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        using var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => false);

        Assert.True((await messenger.SendAsync(bob.IdentityKey, "my own text", cancellationToken)).IsQueued);

        Assert.Empty(notifier.Shown);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task RepeatedMessage_SecondCopy_ShowsNothingMore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        using var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => false);
        var text = Text(1, "hello");

        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, text, cancellationToken));
        Assert.False(await messenger.Chats.ReceiveAsync(bob.IdentityKey, text, cancellationToken));

        Assert.Single(notifier.Shown);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-19")]
    public async Task Dispose_ThenMessage_ShowsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        await AddAsync(messenger, bob, "Bob", cancellationToken);
        var notifier = new FakeNotifier();
        var alerts = new MessageAlerts(messenger, notifier, new BackgroundSettings(new MemorySettings()), () => false);

        alerts.Dispose();
        alerts.Dispose();
        Assert.True(await messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(1, "hello"), cancellationToken));

        Assert.Empty(notifier.Shown);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task Constructor_MissingPart_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        var settings = new BackgroundSettings(new MemorySettings());

        Assert.Throws<ArgumentNullException>(() => new MessageAlerts(null!, new FakeNotifier(), settings, () => false));
        Assert.Throws<ArgumentNullException>(() => new MessageAlerts(messenger, null!, settings, () => false));
        Assert.Throws<ArgumentNullException>(() => new MessageAlerts(messenger, new FakeNotifier(), null!, () => false));
        Assert.Throws<ArgumentNullException>(() => new MessageAlerts(messenger, new FakeNotifier(), settings, null!));
        messenger.Identity.Dispose();
    }

    private static async Task AddAsync(Messenger messenger, KeySet contact, string name, CancellationToken cancellationToken)
    {
        Assert.True((await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(contact, name)), cancellationToken)).IsSaved);
        Assert.True(await messenger.Contacts.ConfirmFirstConnectionAsync(contact.IdentityKey, contact.StaticKey, cancellationToken));
    }

    private static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddMinutes(sequence), text, out var message));
        return message;
    }
}
