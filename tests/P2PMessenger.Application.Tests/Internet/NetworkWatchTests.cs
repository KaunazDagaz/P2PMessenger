using System.Collections.Concurrent;
using System.Net;
using P2PMessenger.Application.Internet;
using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Tests.Internet;

[Trait("Story", "US-09")]
public sealed class NetworkWatchTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly LocalInterface Wifi = new("wlan0", 3, IPAddress.Parse("192.168.1.20"), 24, true);
    private static readonly LocalInterface OtherWifi = new("wlan0", 3, IPAddress.Parse("10.0.5.7"), 24, true);
    private static readonly LocalInterface Mobile = new("rmnet_data0", 4, IPAddress.Parse("100.76.12.9"), 30, false);
    private static readonly LocalInterface Vpn = new("tun0", 7, IPAddress.Parse("10.8.0.2"), 24, false);

    private readonly ManualTimers timers = new();
    private readonly ConcurrentQueue<bool> told = new();

    [Fact]
    public void Observe_FirstNetwork_TellsNothing()
    {
        using var watch = Start();

        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        Assert.Empty(told);
        Assert.Null(timers.Single.Due);
    }

    [Fact]
    public void Observe_NoNetworkAtTheStart_TellsAtOnce()
    {
        using var watch = Start();

        watch.Observe(NetworkSnapshot.None);

        Assert.Equal([false], told);
    }

    [Fact]
    public void Observe_NoSnapshotYet_IsIgnored()
    {
        using var watch = Start();

        watch.Observe(null);
        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        Assert.Empty(told);
        Assert.Null(timers.Single.Due);
    }

    [Fact]
    public void Observe_WifiToMobileData_TellsAfterTheNetworkSettled()
    {
        using var watch = Start();
        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        watch.Observe(NetworkSnapshot.Create([Mobile], false));

        Assert.Empty(told);
        Assert.Equal(Settle, timers.Single.Due);
        timers.Single.Fire();
        Assert.Equal([true], told);
    }

    [Theory]
    [InlineData("another Wi-Fi")]
    [InlineData("a VPN next to the mobile data")]
    [InlineData("no network")]
    public void Observe_AnyChangeOfTheInterfaces_IsTold(string change)
    {
        using var watch = Start();
        var before = change == "another Wi-Fi" ? NetworkSnapshot.Create([Wifi], false) : NetworkSnapshot.Create([Mobile], false);
        var after = change switch
        {
            "another Wi-Fi" => NetworkSnapshot.Create([OtherWifi], false),
            "a VPN next to the mobile data" => NetworkSnapshot.Create([Mobile, Vpn], false),
            _ => NetworkSnapshot.None,
        };
        watch.Observe(before);

        watch.Observe(after);
        timers.Single.Fire();

        Assert.Equal([after.HasAnyNetwork], told);
    }

    [Fact]
    public void Observe_SameNetworkAgain_TellsNothing()
    {
        using var watch = Start();
        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        Assert.Null(timers.Single.Due);
        Assert.Empty(told);
    }

    [Fact]
    public void Observe_SeveralChangesBeforeTheNetworkSettled_TellsOnceWithTheLastOne()
    {
        using var watch = Start();
        watch.Observe(NetworkSnapshot.Create([Wifi], false));

        watch.Observe(NetworkSnapshot.None);
        watch.Observe(NetworkSnapshot.Create([Mobile], false));

        Assert.Equal(2, timers.Single.Changes);
        timers.Single.Fire();
        Assert.Equal([true], told);
    }

    [Fact]
    public void Dispose_BeforeTheNetworkSettled_TellsNothing()
    {
        var watch = Start();
        watch.Observe(NetworkSnapshot.Create([Wifi], false));
        watch.Observe(NetworkSnapshot.Create([Mobile], false));

        watch.Dispose();
        timers.Single.Fire();
        watch.Observe(NetworkSnapshot.None);

        Assert.Empty(told);
        Assert.True(timers.Single.Disposed);
    }

    private NetworkWatch Start() => new(Settle, timers, told.Enqueue);

    private sealed class ManualTimers : TimeProvider
    {
        private readonly List<ManualTimer> created = [];

        public ManualTimer Single => Assert.Single(created);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(callback, state);
            timer.Change(dueTime, period);
            timer.Changes = 0;
            created.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback callback;
        private readonly object? state;

        public ManualTimer(TimerCallback callback, object? state)
        {
            this.callback = callback;
            this.state = state;
        }

        public TimeSpan? Due { get; private set; }

        public int Changes { get; set; }

        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : dueTime;
            Changes++;
            return !Disposed;
        }

        public void Fire()
        {
            Due = null;
            callback(state);
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
