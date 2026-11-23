using System.Text;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Core.Identity;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class DiscoveryTests
{
    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-06")]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-11")]
    public async Task ThreeContactsOnOneNetwork_FindEachOtherWithoutAnAddressAndChatInEveryDirection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        AppPeer[] peers = [alice, bob, carol];
        foreach (var peer in peers)
        {
            foreach (var other in peers.Where(other => !ReferenceEquals(other, peer)))
            {
                await peer.ImportAsync(other, cancellationToken);
            }
        }

        await Eventually.HoldsAsync(
            () => peers.All(peer => peers.Where(other => !ReferenceEquals(other, peer)).All(other =>
                peer.ContactFor(other).IsAdded && peer.Messenger.Connections.ReachabilityOf(other.Identity.Keys.IdentityKey) == Reachability.Nearby)),
            cancellationToken);

        foreach (var peer in peers)
        {
            foreach (var other in peers.Where(other => !ReferenceEquals(other, peer)))
            {
                var text = $"{peer.Identity.Name.Value} to {other.Identity.Name.Value}";
                Assert.Equal(SendOutcome.Queued, (await peer.Messenger.SendAsync(other.Identity.Keys.IdentityKey, text, cancellationToken)).Outcome);
            }
        }

        await Eventually.HoldsAsync(
            () => peers.All(peer => peers.Where(other => !ReferenceEquals(other, peer)).All(other =>
                peer.Messenger.Chats.MessagesWith(other.Identity.Keys.IdentityKey).Count == 2
                && peer.Messenger.Chats.MessagesWith(other.Identity.Keys.IdentityKey).All(static message => message.Direction == MessageDirection.Incoming || message.Delivery == DeliveryState.Received))),
            cancellationToken);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-06")]
    [Trait("NFR", "NFR-14")]
    public async Task DataSentWhileLookingForContacts_HoldsNoNamesKeysCodesOrMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string secret = "Meet at the old water tower";
        await using var alice = await AppPeer.StartAsync("Alice Distinctive-Name", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob Unmistakable", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol Recognisable", cancellationToken);
        await alice.ImportAsync(bob, cancellationToken);
        await alice.ImportAsync(carol, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);
        await carol.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && alice.ContactFor(carol).IsAdded, cancellationToken);
        Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, secret, cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(alice.Identity.Keys.IdentityKey).Count == 1, cancellationToken);
        await Task.Delay(300, cancellationToken);

        var sent = AppPeer.HubOfThisTest.Sent;

        Assert.True(sent.Count >= 20, $"Only {sent.Count} datagrams were sent.");
        Assert.All(sent, static datagram => Assert.Equal(Announcement.Length, datagram.Length));
        List<byte[]> forbidden = [Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("P2PM1-")];
        foreach (var peer in new[] { alice, bob, carol })
        {
            var name = peer.Identity.Name.Value;
            forbidden.Add(Encoding.UTF8.GetBytes(name));
            forbidden.Add(Encoding.Unicode.GetBytes(name));
            forbidden.Add(Encoding.UTF8.GetBytes(name.Split(' ')[1]));
            forbidden.Add(Encoding.UTF8.GetBytes(peer.Identity.CodeText[..16]));
            forbidden.Add(peer.Identity.Code.ToBytes()[..16]);
            foreach (var key in new[] { peer.Identity.Keys.IdentityKey.Bytes.ToArray(), peer.Identity.Keys.StaticKey.Bytes.ToArray(), peer.Identity.Keys.OnionKey.Bytes.ToArray() })
            {
                forbidden.Add(key);
                forbidden.Add(key[..8]);
            }
        }

        foreach (var datagram in sent)
        {
            foreach (var value in forbidden)
            {
                Assert.Equal(-1, datagram.AsSpan().IndexOf(value));
            }
        }

        var distinct = sent.Select(static datagram => Convert.ToHexString(datagram)).Distinct().ToList();
        var nonces = distinct.Select(static hex => hex[..(Announcement.NonceLength * 2)]).Distinct().Count();
        Assert.Equal(distinct.Count, nonces);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-03")]
    public async Task ContactLeavesAndComesBack_IsNotReachableThenReachableNearbyAndGetsTheWaitingMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bobKeys = KeySet.Create();
        var exportedBob = Export(bobKeys);
        var bobKey = bobKeys.IdentityKey;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        var bob = await AppPeer.StartAsync(bobKeys, "Bob", cancellationToken);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        try
        {
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            await Eventually.HoldsAsync(() => alice.Messenger.Connections.ReachabilityOf(bobKey) == Reachability.Nearby, cancellationToken);

            await bob.DisposeAsync();
            await Eventually.HoldsAsync(
                () => alice.Messenger.Connections.ReachabilityOf(bobKey) == Reachability.NotReachable
                    && !alice.Messenger.Connections.IsHeard(bobKey)
                    && alice.Messenger.Connections.DialStatusOf(bobKey) == DialStatus.None,
                cancellationToken);
            Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, "back yet?", cancellationToken)).Outcome);

            Assert.True(KeySet.TryImport(exportedBob, out var bobAgain));
            bob = await AppPeer.StartAsync(bobAgain, "Bob", cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);

            await Eventually.HoldsAsync(() => alice.Messenger.Connections.ReachabilityOf(bobKey) == Reachability.Nearby, cancellationToken);
            await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Single().Delivery == DeliveryState.Received, cancellationToken);
            Assert.Equal("back yet?", bob.Messenger.Chats.MessagesWith(aliceKey).Single().Text);
        }
        finally
        {
            await bob.DisposeAsync();
        }
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-06")]
    public async Task LogsOfDiscovery_HoldNoAddressNameOrKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var capture = new LogCapture();
        using var factory = new CapturingLoggerFactory(capture);
        await using var alice = await AppPeer.StartAsync(KeySet.Create(), "Alice", setup => setup with { Loggers = factory }, cancellationToken);
        await using var bob = await AppPeer.StartAsync(KeySet.Create(), "Bob", setup => setup with { Loggers = factory }, cancellationToken);
        await alice.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.Messenger.Connections.IsConnected(bob.Identity.Keys.IdentityKey), cancellationToken);
        bob.Node.InRange = false;
        await Task.Delay(300, cancellationToken);

        Assert.Contains(capture.Entries, static entry => entry.EventId.Name == "DiscoveryStarted");
        Assert.Contains(capture.Entries, static entry => entry.EventId.Name == "NetworkChanged");
        List<string> forbidden = ["127.0.0.1", "10.250.0.1", "Alice", "Bob"];
        foreach (var keys in new[] { alice.Identity.Keys, bob.Identity.Keys })
        {
            foreach (var key in new[] { keys.IdentityKey.Bytes.ToArray(), keys.StaticKey.Bytes.ToArray(), keys.OnionKey.Bytes.ToArray() })
            {
                forbidden.Add(Convert.ToHexString(key));
                forbidden.Add(Convert.ToBase64String(key));
            }
        }

        foreach (var entry in capture.Entries)
        {
            Assert.Null(entry.Exception);
            foreach (var text in entry.Values.Append(entry.Message))
            {
                foreach (var value in forbidden)
                {
                    Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private static byte[] Export(KeySet keys)
    {
        var exported = new byte[KeySet.ExportLength];
        Assert.True(keys.TryExport(exported, out _));
        return exported;
    }

    private sealed class CapturingLoggerFactory(LogCapture capture) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger<object>(capture);

        public void Dispose()
        {
        }
    }
}
