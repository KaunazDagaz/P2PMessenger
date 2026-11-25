using System.Text.Json;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application;
using P2PMessenger.Application.Logging;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class LogFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "p2pm-logfile-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task LogFile_OfTwoAppsThatChat_HoldsTheEventsButNoNameAddressKeyOrText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string secret = "Meet at the old water tower";
        using var loggers = new FileLoggerFactory(new LogFileOptions { Folder = folder, Minimum = LogLevel.Trace }, TimeProvider.System);
        await using var alice = await AppPeer.StartAsync(KeySet.Create(), "Alice Distinctive-Name", setup => setup with { Loggers = loggers }, cancellationToken);
        await using var bob = await AppPeer.StartAsync(KeySet.Create(), "Bob Unmistakable", setup => setup with { Loggers = loggers }, cancellationToken);
        await alice.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);
        Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, secret, cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(alice.Identity.Keys.IdentityKey).Count == 1, cancellationToken);

        var text = ReadAll(loggers.CurrentFile);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        var events = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            Assert.True(json.RootElement.TryGetProperty("time", out _));
            Assert.True(json.RootElement.TryGetProperty("level", out _));
            Assert.True(json.RootElement.TryGetProperty("category", out _));
            if (json.RootElement.TryGetProperty("event", out var name))
            {
                events.Add(name.GetString() ?? string.Empty);
            }
        }

        Assert.Contains("Listening", events);
        Assert.Contains("Established", events);
        foreach (var value in Forbidden(secret, alice, bob))
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<string> Forbidden(string secret, params AppPeer[] peers)
    {
        List<string> forbidden = [secret, "Distinctive", "Unmistakable", "bridge", "127.0.0.1", "::1", "localhost", "P2PM1-", ".onion"];
        foreach (var peer in peers)
        {
            var keys = peer.Identity.Keys;
            foreach (var key in new[] { keys.IdentityKey.Bytes.ToArray(), keys.StaticKey.Bytes.ToArray(), keys.OnionKey.Bytes.ToArray() })
            {
                forbidden.Add(Convert.ToHexString(key));
                forbidden.Add(Convert.ToBase64String(key));
            }

            forbidden.Add(keys.OnionAddress.Label);
        }

        return forbidden;
    }

    private static string ReadAll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
