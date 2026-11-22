using System.Diagnostics;
using System.Net;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Tests.Delivery;

public sealed class OutboxTests
{
    [Fact]
    public void DeliveryOptions_Defaults_WakeEveryTenSecondsAndResendAfterThirty()
    {
        var options = new DeliveryOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), options.WakeUpInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ResendAfter);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(10, 0)]
    [InlineData(-1, 30)]
    [InlineData(10, 86_401)]
    public async Task StartAsync_DeliveryTimesOutOfRange_AreRefused(int wakeUpSeconds, int resendSeconds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = TestKeys.NewIdentity("Alice");
        var setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Delivery = new DeliveryOptions { WakeUpInterval = TimeSpan.FromSeconds(wakeUpSeconds), ResendAfter = TimeSpan.FromSeconds(resendSeconds) },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Messenger.StartAsync(identity, setup, cancellationToken));
    }

    [Fact]
    public async Task StartAsync_WakeUpScheduler_IsUsedWithTheIntervalAndReleasedOnDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wakeUps = new ManualWakeUps();
        using var identity = TestKeys.NewIdentity("Alice");
        var setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            WakeUps = wakeUps,
            Delivery = new DeliveryOptions { WakeUpInterval = TimeSpan.FromSeconds(7) },
        };

        var messenger = await Messenger.StartAsync(identity, setup, cancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(7), wakeUps.Interval);
        Assert.Equal(1, wakeUps.Active);
        wakeUps.Fire();

        await messenger.DisposeAsync();

        Assert.Equal(0, wakeUps.Active);
    }

    [Fact]
    public async Task TimerWakeUpScheduler_Every_CallsBackUntilDisposed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scheduler = new TimerWakeUpScheduler(TimeProvider.System);
        var calls = 0;

        var timer = scheduler.Every(TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref calls));
        await TestMessengers.WaitUntilAsync(() => Volatile.Read(ref calls) >= 2, cancellationToken);
        timer.Dispose();
        await Task.Delay(100, cancellationToken);
        var afterDispose = Volatile.Read(ref calls);
        await Task.Delay(200, cancellationToken);

        Assert.Equal(afterDispose, Volatile.Read(ref calls));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Every(TimeSpan.Zero, static () => { }));
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-03")]
    [Trait("NFR", "NFR-07")]
    public async Task WaitingMessages_ContactBecomesReachable_AreSentWithin30SecondsInOrderAndReceived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pair = await Pair.ConnectAsync(cancellationToken);
        await pair.DisconnectAsync(cancellationToken);

        foreach (var text in new[] { "one", "two", "three" })
        {
            Assert.Equal(SendOutcome.Queued, (await pair.Alice.SendAsync(pair.BobKey, text, cancellationToken)).Outcome);
        }

        await Task.Delay(200, cancellationToken);
        Assert.Empty(pair.Bob.Chats.MessagesWith(pair.AliceKey));
        Assert.All(pair.Alice.Chats.MessagesWith(pair.BobKey), static message => Assert.Equal(DeliveryState.Waiting, message.Delivery));

        pair.Alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);
        await TestMessengers.WaitUntilAsync(() => pair.Alice.Connections.IsConnected(pair.BobKey), cancellationToken);
        var reachable = Stopwatch.StartNew();
        await TestMessengers.WaitUntilAsync(
            () => pair.Alice.Chats.MessagesWith(pair.BobKey).All(static message => message.Delivery == DeliveryState.Received),
            cancellationToken);

        Assert.True(reachable.Elapsed < TimeSpan.FromSeconds(30), $"Delivered after {reachable.Elapsed}.");
        Assert.Equal(["one", "two", "three"], pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => message.Text));
        Assert.Equal(
            pair.Alice.Chats.MessagesWith(pair.BobKey).Select(static message => (message.Id, message.Sequence)),
            pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => (message.Id, message.Sequence)));
        Assert.Empty(pair.Alice.Chats.ContactsWithWaitingMessages());
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    public async Task WaitingMessage_DaysLater_StillWaitsAndIsSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime(DateTimeOffset.UtcNow);
        await using var pair = await Pair.ConnectAsync(time, cancellationToken);
        await pair.DisconnectAsync(cancellationToken);
        var sent = await pair.Alice.SendAsync(pair.BobKey, "see you", cancellationToken);
        Assert.True(sent.IsQueued);

        time.Advance(TimeSpan.FromDays(400));
        pair.Alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);

        await TestMessengers.WaitUntilAsync(
            () => pair.Alice.Chats.MessagesWith(pair.BobKey).Single().Delivery == DeliveryState.Received,
            cancellationToken);
        var arrived = Assert.Single(pair.Bob.Chats.MessagesWith(pair.AliceKey));
        Assert.Equal(sent.Message.Id, arrived.Id);
        Assert.Equal(sent.Message.WrittenAt, arrived.WrittenAt);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public async Task SendAsync_ConnectedContact_IsReceivedByTheContactsAppOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pair = await Pair.ConnectAsync(cancellationToken);
        var changes = new List<ChatChangedEventArgs>();
        pair.Alice.Chats.Changed += (_, change) =>
        {
            lock (changes)
            {
                changes.Add(change);
            }
        };

        var sent = await pair.Alice.SendAsync(pair.BobKey, "hello", cancellationToken);

        Assert.Equal(SendOutcome.Queued, sent.Outcome);
        Assert.True(sent.IsQueued);
        var sentId = sent.Message.Id;
        await TestMessengers.WaitUntilAsync(() => pair.Alice.Chats.MessagesWith(pair.BobKey).Single().Delivery == DeliveryState.Received, cancellationToken);
        Assert.Equal("hello", Assert.Single(pair.Bob.Chats.MessagesWith(pair.AliceKey)).Text);
        Assert.Equal(DeliveryState.None, pair.Bob.Chats.MessagesWith(pair.AliceKey).Single().Delivery);
        lock (changes)
        {
            Assert.Contains(changes, change => change.Updated is { Delivery: DeliveryState.Received } updated && updated.Id.Equals(sentId));
        }
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaitingAsync_BeforeTheContactIsReachable_IsNeverSentAndTheNumbersGoOn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pair = await Pair.ConnectAsync(cancellationToken);
        await pair.DisconnectAsync(cancellationToken);
        var kept = await pair.Alice.SendAsync(pair.BobKey, "keep", cancellationToken);
        var dropped = await pair.Alice.SendAsync(pair.BobKey, "drop", cancellationToken);

        Assert.True(await pair.Alice.DeleteWaitingAsync(pair.BobKey, dropped.Message!.Id, cancellationToken));
        var after = await pair.Alice.SendAsync(pair.BobKey, "after", cancellationToken);
        pair.Alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);
        await TestMessengers.WaitUntilAsync(
            () => pair.Alice.Chats.MessagesWith(pair.BobKey).All(static message => message.Delivery == DeliveryState.Received),
            cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.Equal(["keep", "after"], pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => message.Text));
        Assert.Equal([1L, 3L], pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => message.Sequence));
        Assert.Equal([kept.Message!.Id, after.Message!.Id], pair.Alice.Chats.MessagesWith(pair.BobKey).Select(static message => message.Id));
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-11")]
    public async Task DeleteChatAsync_WithAWaitingMessage_CancelsItAndTheContactKeepsItsCopyInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pair = await Pair.ConnectAsync(cancellationToken);
        await pair.Alice.SendAsync(pair.BobKey, "one", cancellationToken);
        await TestMessengers.WaitUntilAsync(() => pair.Alice.Chats.MessagesWith(pair.BobKey).Single().Delivery == DeliveryState.Received, cancellationToken);
        await pair.DisconnectAsync(cancellationToken);
        await pair.Alice.SendAsync(pair.BobKey, "two", cancellationToken);

        Assert.Equal(2, await pair.Alice.DeleteChatAsync(pair.BobKey, cancellationToken));
        Assert.Empty(pair.Alice.Chats.MessagesWith(pair.BobKey));
        await pair.Alice.SendAsync(pair.BobKey, "three", cancellationToken);
        pair.Alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);
        await TestMessengers.WaitUntilAsync(() => pair.Alice.Chats.MessagesWith(pair.BobKey).Single().Delivery == DeliveryState.Received, cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.Equal(["one", "three"], pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => message.Text));
        Assert.Equal([1L, 3L], pair.Bob.Chats.MessagesWith(pair.AliceKey).Select(static message => message.Sequence));
        Assert.Equal("three", Assert.Single(pair.Alice.Chats.MessagesWith(pair.BobKey)).Text);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaitingAsync_AfterAPassTookTheMessageButBeforeItsTurn_IsNotSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var pair = await Pair.ConnectAsync(cancellationToken);
        await pair.DisconnectAsync(cancellationToken);
        var dropped = await pair.Alice.SendAsync(pair.BobKey, "drop", cancellationToken);

        var turn = await pair.Alice.Chats.EnterDeliveryAsync(pair.BobKey, cancellationToken);
        var deleting = pair.Alice.DeleteWaitingAsync(pair.BobKey, dropped.Message!.Id, cancellationToken);
        pair.Alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);
        await TestMessengers.WaitUntilAsync(() => pair.Alice.Connections.IsConnected(pair.BobKey), cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.False(deleting.IsCompleted);
        turn.Dispose();

        Assert.True(await deleting);
        await pair.Alice.SendAsync(pair.BobKey, "after", cancellationToken);
        await TestMessengers.WaitUntilAsync(() => pair.Bob.Chats.MessagesWith(pair.AliceKey).Count > 0, cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.Equal("after", Assert.Single(pair.Bob.Chats.MessagesWith(pair.AliceKey)).Text);
    }

    private sealed class Pair : IAsyncDisposable
    {
        private Pair(Messenger alice, Messenger bob)
        {
            Alice = alice;
            Bob = bob;
        }

        public Messenger Alice { get; }

        public Messenger Bob { get; }

        public Ed25519PublicKey AliceKey => Alice.Identity.Keys.IdentityKey;

        public Ed25519PublicKey BobKey => Bob.Identity.Keys.IdentityKey;

        public IPEndPoint BobEndpoint => new(IPAddress.Loopback, Bob.Connections.ListeningPort ?? throw new InvalidOperationException("Bob is not listening."));

        public static Task<Pair> ConnectAsync(CancellationToken cancellationToken) => ConnectAsync(TimeProvider.System, cancellationToken);

        public static async Task<Pair> ConnectAsync(TimeProvider aliceTime, CancellationToken cancellationToken)
        {
            var alice = await Messenger.StartAsync(
                TestKeys.NewIdentity("Alice"),
                MessengerSetup.InMemory() with
                {
                    ListenAddress = IPAddress.Loopback,
                    ListenPort = 0,
                    Connection = TestMessengers.Fast,
                    TimeProvider = aliceTime,
                },
                cancellationToken);
            var bob = await TestMessengers.StartAsync("Bob", cancellationToken);
            var pair = new Pair(alice, bob);
            Assert.True((await alice.Contacts.AddAsync(alice.Contacts.Check(bob.Identity.CodeText), cancellationToken)).IsSaved);
            Assert.True((await bob.Contacts.AddAsync(bob.Contacts.Check(alice.Identity.CodeText), cancellationToken)).IsSaved);
            alice.Connections.SetAddress(pair.BobKey, pair.BobEndpoint);
            await TestMessengers.WaitUntilAsync(
                () => alice.Contacts.TryGet(pair.BobKey, out var contact) && contact.IsAdded && alice.Connections.IsConnected(pair.BobKey)
                    && bob.Contacts.TryGet(pair.AliceKey, out var other) && other.IsAdded,
                cancellationToken);
            return pair;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Alice.Connections.SetAddress(BobKey, null);
            if (Alice.Connections.TryGetConnection(BobKey, out var connection))
            {
                connection.Close();
            }

            await TestMessengers.WaitUntilAsync(() => !Alice.Connections.IsConnected(BobKey) && !Bob.Connections.IsConnected(AliceKey), cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Alice.DisposeAsync();
            await Bob.DisposeAsync();
            Alice.Identity.Dispose();
            Bob.Identity.Dispose();
        }
    }
}
