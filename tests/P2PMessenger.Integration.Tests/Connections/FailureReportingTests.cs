using P2PMessenger.Core;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;
using static P2PMessenger.Integration.Tests.Connections.TestContacts;

namespace P2PMessenger.Integration.Tests.Connections;

public sealed class FailureReportingTests
{
    [Fact(Timeout = 30_000)]
    public async Task Connect_DialerThatThrows_ReportsTheFailureToTheSinkAndRefuses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sink = new RecordingFailureSink();
        await using var alice = TestPeer.Start(failures: sink);
        await using var bob = TestPeer.Start();
        var bobForAlice = alice.Know(bob);

        var result = await alice.Manager.ConnectAsync(bobForAlice, new ThrowingDialer(), cancellationToken);

        Assert.False(result.IsConnected);
        var report = Assert.Single(sink.Reports);
        Assert.Equal(FailureSource.Connections, report.Source);
        Assert.IsType<InvalidOperationException>(report.Exception);
    }

    [Fact(Timeout = 30_000)]
    public async Task Message_ObserverThatThrows_ReportsTheFailureToTheSink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sink = new RecordingFailureSink();
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start(failures: sink);
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        bob.Observer.OnMessage = static (_, _) => throw new InvalidOperationException("The observer failed.");
        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);

        Assert.True(await result.Connection.SendAsync(Text(1, "hello"), cancellationToken));

        Assert.True(await Raw.EventuallyAsync(() => sink.Reports.Count > 0, TimeSpan.FromSeconds(10), cancellationToken));
        Assert.All(sink.Reports, static report =>
        {
            Assert.Equal(FailureSource.Connections, report.Source);
            Assert.IsType<InvalidOperationException>(report.Exception);
        });
    }

    [Fact(Timeout = 30_000)]
    public async Task Message_ObserverThatThrowsWithoutASink_LogsTheFailureLocally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var log = new LogCapture();
        await using var alice = TestPeer.Start();
        await using var bob = TestPeer.Start(log: log);
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        bob.Observer.OnMessage = static (_, _) => throw new InvalidOperationException("The observer failed.");
        var result = await alice.ConnectAsync(bobForAlice, bob, cancellationToken);
        Assert.True(result.IsConnected);

        Assert.True(await result.Connection.SendAsync(Text(1, "hello"), cancellationToken));

        Assert.True(await Raw.EventuallyAsync(() => log.Entries.Any(static entry => entry.EventId.Name == "ObserverFailed"), TimeSpan.FromSeconds(10), cancellationToken));
    }

    private sealed class ThrowingDialer : IDialer
    {
        public ConnectionRoute Route => ConnectionRoute.Nearby;

        public ValueTask<Stream?> DialAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The dial failed.");
    }
}
