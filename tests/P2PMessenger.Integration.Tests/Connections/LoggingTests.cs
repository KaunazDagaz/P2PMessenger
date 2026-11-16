using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
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
