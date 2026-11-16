using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Integration.Tests.Connections;

public sealed class FloodTests
{
    private static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 60_000)]
    [Trait("NFR", "NFR-09")]
    public async Task PendingConnectionsFromOneSource_BeyondTheLimit_AreClosedAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TestOptions.Fast with
        {
            HintTimeout = TimeSpan.FromSeconds(20),
            HandshakeTimeout = TimeSpan.FromSeconds(20),
            Admission = TestOptions.Generous with { MaxPendingPerSource = 2 },
        };
        await using var bob = TestPeer.Start(options);
        await using var alice = TestPeer.Start();
        var bobForAlice = alice.Know(bob);
        bob.Know(alice);
        using var first = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        using var second = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        await Task.Delay(300, cancellationToken);

        using (var third = await Raw.ConnectAsync(bob.Endpoint, cancellationToken))
        {
            Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(third.GetStream(), CloseLimit, cancellationToken));
        }

        Assert.Equal(Raw.StillOpen, await Raw.CountBytesUntilClosedAsync(first.GetStream(), TimeSpan.FromMilliseconds(300), cancellationToken));
        first.Dispose();
        second.Dispose();
        Assert.True(await ConnectsWithinAsync(alice, bobForAlice, bob, TimeSpan.FromSeconds(10), cancellationToken));
    }

    [Fact(Timeout = 60_000)]
    [Trait("NFR", "NFR-09")]
    public async Task HandshakesFromOneSource_FasterThanTheRate_AreClosedAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TestOptions.Fast with
        {
            HintTimeout = TimeSpan.FromSeconds(20),
            HandshakeTimeout = TimeSpan.FromSeconds(20),
            Admission = TestOptions.Generous with { HandshakesPerSecondPerSource = 0.01, HandshakeBurstPerSource = 2 },
        };
        await using var bob = TestPeer.Start(options);
        using var first = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        using var second = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);
        await Task.Delay(300, cancellationToken);

        using var third = await Raw.ConnectAsync(bob.Endpoint, cancellationToken);

        Assert.Equal(0, await Raw.CountBytesUntilClosedAsync(third.GetStream(), CloseLimit, cancellationToken));
        Assert.Equal(Raw.StillOpen, await Raw.CountBytesUntilClosedAsync(first.GetStream(), TimeSpan.FromMilliseconds(300), cancellationToken));
        Assert.Equal(Raw.StillOpen, await Raw.CountBytesUntilClosedAsync(second.GetStream(), TimeSpan.FromMilliseconds(300), cancellationToken));
        Assert.Equal(0, bob.Observer.ConnectedCount);
    }

    private static async Task<bool> ConnectsWithinAsync(TestPeer caller, SessionContact contact, TestPeer callee, TimeSpan limit, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + limit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await caller.ConnectAsync(contact, callee, cancellationToken);
            if (result.IsConnected)
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }
}
