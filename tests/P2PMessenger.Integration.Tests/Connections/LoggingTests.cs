using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tor;
using static P2PMessenger.Integration.Tests.Connections.TestContacts;

namespace P2PMessenger.Integration.Tests.Connections;

public sealed class LoggingTests
{
    [Fact(Timeout = 30_000)]
    public async Task Logs_OfConnectionsMessagesAndRefusals_HoldNoAddressKeyOrText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string secret = "The meeting point is the old bridge";
        var log = new LogCapture();
        await using var alice = TestPeer.Start(log: log);
        await using var bob = TestPeer.Start(log: log);
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        using var stranger = KeySet.Create();
        using var bobForStranger = Create(stranger, bob.Keys);

        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);
        Assert.True(await result.Connection.SendAsync(Text(1, secret), cancellationToken));
        Assert.Equal(secret, Assert.IsType<TextMessage>((await bob.Observer.NextMessageAsync(cancellationToken)).Message).Text);
        using (var initiator = SessionInitiator.Start(stranger, bobForStranger))
        using (var client = await Raw.ConnectAsync(bob.Endpoint, cancellationToken))
        {
            await client.GetStream().WriteAsync(initiator.FirstFlight.ToArray(), cancellationToken);
            Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(client.GetStream(), TimeSpan.FromSeconds(5), cancellationToken));
        }

        result.Connection.Close();
        await bob.Observer.NextDisconnectedAsync(cancellationToken);

        foreach (var name in new[] { "Listening", "Established", "Refused", "Closed" })
        {
            Assert.True(await Raw.EventuallyAsync(() => log.Entries.Any(entry => entry.EventId.Name == name), TimeSpan.FromSeconds(5), cancellationToken), $"No {name} entry.");
        }

        var entries = log.Entries;
        var forbidden = Forbidden(secret, alice.Keys, bob.Keys, stranger);
        foreach (var entry in entries)
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

    [Fact(Timeout = 30_000)]
    [Trait("Story", "US-09")]
    public async Task Logs_OfConnectionsThroughTor_NameTheRouteAndTheReplyButNoOnionAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var log = new LogCapture();
        await using var alice = TestPeer.Start(log: log);
        await using var bob = TestPeer.Start(log: log);
        await using var carol = TestPeer.Start(log: log);
        var bobForAlice = alice.Know(bob);
        var carolForAlice = alice.Know(carol);
        bob.Know(alice);
        await using var onion = OnionListener.Start(bob.Manager, null, new CapturingLogger<OnionListener>(log));
        await using var proxy = new FakeSocksProxy();
        proxy.Route(bob.Keys.OnionAddress, onion.Target);

        var tor = await alice.Manager.ConnectAsync(bobForAlice, new OnionDialer(proxy.Endpoint, bob.Keys.OnionAddress, new CapturingLogger<OnionDialer>(log)), cancellationToken);
        var missing = await alice.Manager.ConnectAsync(carolForAlice, new OnionDialer(proxy.Endpoint, carol.Keys.OnionAddress, new CapturingLogger<OnionDialer>(log)), cancellationToken);

        Assert.True(tor.IsConnected);
        Assert.False(missing.IsConnected);
        Assert.Contains(log.Entries, static entry => entry.EventId.Name == "OnionDialFailed" && entry.Message.Contains(nameof(SocksFailure.OnionDescriptorNotFound), StringComparison.Ordinal));
        Assert.True(await Raw.EventuallyAsync(() => log.Entries.Count(static entry => entry.EventId.Name == "Established" && entry.Message.Contains(nameof(ConnectionRoute.Tor), StringComparison.Ordinal)) == 2, TimeSpan.FromSeconds(5), cancellationToken));
        var forbidden = Forbidden("no secret here", alice.Keys, bob.Keys, carol.Keys);
        forbidden.Add(bob.Keys.OnionAddress.Label);
        forbidden.Add(carol.Keys.OnionAddress.Label);
        forbidden.Add(".onion");
        foreach (var entry in log.Entries)
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

    private static List<string> Forbidden(string secret, params KeySet[] keySets)
    {
        List<string> forbidden = [secret, "bridge", "127.0.0.1", "::1", "localhost"];
        foreach (var keys in keySets)
        {
            foreach (var key in new[] { keys.IdentityKey.Bytes.ToArray(), keys.StaticKey.Bytes.ToArray(), keys.OnionKey.Bytes.ToArray() })
            {
                forbidden.Add(Convert.ToHexString(key));
                forbidden.Add(Convert.ToBase64String(key));
            }
        }

        return forbidden;
    }
}
