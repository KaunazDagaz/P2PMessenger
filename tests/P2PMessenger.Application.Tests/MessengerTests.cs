using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests;

public sealed class MessengerTests
{
    [Fact]
    public async Task StartAsync_PortZero_ListensOnAFreePort()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);

        Assert.Equal(ListenResult.Listening, messenger.Connections.LastListenResult);
        Assert.True(messenger.Connections.ListeningPort > 0);
        Assert.Empty(messenger.Contacts.All);
        messenger.Identity.Dispose();
    }

    [Theory]
    [Trait("Story", "US-11")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task SendAsync_EmptyText_IsNotSent(string? text)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();

        var result = await messenger.SendAsync(bob.IdentityKey, text, cancellationToken);

        Assert.Equal(SendOutcome.Empty, result.Outcome);
        Assert.False(result.IsSent);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task SendAsync_TextLongerThanTheLimit_IsTooLong()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();

        var result = await messenger.SendAsync(bob.IdentityKey, new string('a', TextMessage.MaxUtf8Length + 1), cancellationToken);

        Assert.Equal(SendOutcome.TooLong, result.Outcome);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task SendAsync_LoneSurrogate_IsNotSendable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();

        var result = await messenger.SendAsync(bob.IdentityKey, "a\uD800b", cancellationToken);

        Assert.Equal(SendOutcome.NotSendable, result.Outcome);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task SendAsync_NotAContact_IsNotSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();

        var result = await messenger.SendAsync(bob.IdentityKey, "hello", cancellationToken);

        Assert.Equal(SendOutcome.NotAContact, result.Outcome);
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task SendAsync_ContactWaitingForTheFirstConnection_IsNotSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        Assert.True((await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(bob, "Bob")), cancellationToken)).IsSaved);

        var result = await messenger.SendAsync(bob.IdentityKey, "hello", cancellationToken);

        Assert.Equal(SendOutcome.WaitingForFirstConnection, result.Outcome);
        Assert.Empty(messenger.Chats.MessagesWith(bob.IdentityKey));
        messenger.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-11")]
    public async Task SendAsync_AddedContactNotConnected_IsNotSentAndNotShown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        Assert.True((await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(bob, "Bob")), cancellationToken)).IsSaved);
        Assert.True(await messenger.Contacts.ConfirmFirstConnectionAsync(bob.IdentityKey, bob.StaticKey, cancellationToken));

        var result = await messenger.SendAsync(bob.IdentityKey, "hello", cancellationToken);

        Assert.Equal(SendOutcome.NotConnected, result.Outcome);
        Assert.Empty(messenger.Chats.MessagesWith(bob.IdentityKey));
        Assert.False(messenger.Connections.IsConnected(bob.IdentityKey));
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_Twice_IsHarmless()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);

        await messenger.DisposeAsync();
        await messenger.DisposeAsync();

        Assert.Null(messenger.Connections.ListeningPort);
        messenger.Identity.Dispose();
    }

    [Fact]
    public async Task Contacts_AddedWhileRunning_ReachTheConnectionService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var messenger = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();

        Assert.False(messenger.Connections.SetAddress(bob.IdentityKey, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 1)));
        Assert.True((await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(bob, "Bob")), cancellationToken)).IsSaved);

        Assert.True(messenger.Connections.SetAddress(bob.IdentityKey, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 1)));
        Assert.Equal(ContactState.WaitingForFirstConnection, Assert.Single(messenger.Contacts.All).State);
        messenger.Identity.Dispose();
    }
}
