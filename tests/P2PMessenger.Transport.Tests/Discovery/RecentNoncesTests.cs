using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Transport.Tests.Discovery;

public sealed class RecentNoncesTests
{
    private static readonly byte[] First = [.. Enumerable.Range(0, 16).Select(static value => (byte)value)];
    private static readonly byte[] Second = [.. Enumerable.Range(100, 16).Select(static value => (byte)value)];

    [Fact]
    public void Contains_AddedNonce_UntilItsLifetimeEnds()
    {
        var nonces = new RecentNonces(TimeSpan.FromSeconds(30), TimeSpan.TicksPerSecond, 16);

        nonces.Add(First, 0);

        Assert.True(nonces.Contains(First, TimeSpan.FromSeconds(29).Ticks));
        Assert.False(nonces.Contains(Second, 0));
        Assert.False(nonces.Contains(First, TimeSpan.FromSeconds(30).Ticks));
        nonces.Prune(TimeSpan.FromSeconds(31).Ticks);
        Assert.Equal(0, nonces.Count);
    }

    [Fact]
    public void Add_AtCapacity_DropsTheOldestFirst()
    {
        var nonces = new RecentNonces(TimeSpan.FromSeconds(30), TimeSpan.TicksPerSecond, 2);
        byte[] third = [.. Enumerable.Range(200, 16).Select(static value => (byte)value)];

        nonces.Add(First, 0);
        nonces.Add(Second, 10);
        nonces.Add(third, 20);

        Assert.Equal(2, nonces.Count);
        Assert.False(nonces.Contains(First, 21));
        Assert.True(nonces.Contains(Second, 21));
        Assert.True(nonces.Contains(third, 21));
    }
}
