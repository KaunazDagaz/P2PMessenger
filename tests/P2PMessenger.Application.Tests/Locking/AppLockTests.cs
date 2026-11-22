using P2PMessenger.Application.LocalData;
using P2PMessenger.Application.Locking;

namespace P2PMessenger.Application.Tests.Locking;

public sealed class AppLockTests
{
    [Fact]
    [Trait("Story", "US-20")]
    public void BackgroundLimit_IsFiveMinutesByDefault()
    {
        using var appLock = new AppLock(new LockedLocalData(), new ManualClock());

        Assert.Equal(TimeSpan.FromMinutes(5), appLock.BackgroundLimit);
        Assert.Equal(TimeSpan.FromMinutes(5), AppLock.DefaultBackgroundLimit);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Background_ShorterThanTheLimit_DoesNotLock()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);
        var changes = Count(appLock);

        appLock.EnterBackground();
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        appLock.EnterForeground();

        Assert.False(appLock.IsLocked);
        Assert.Equal(0, changes());
        Assert.Equal(0, clock.ActiveTimers());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Background_ForTheLimit_LocksWhileStillInTheBackground()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);
        var changes = Count(appLock);

        appLock.EnterBackground();
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(appLock.IsLocked);
        Assert.True(appLock.IsInBackground);
        Assert.Equal(1, changes());
        appLock.EnterForeground();
        Assert.True(appLock.IsLocked);
        Assert.Equal(1, changes());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Foreground_AfterTheClockMovedOnButTheTimerDidNotFire_Locks()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);

        appLock.EnterBackground();
        clock.MoveClockOnly(TimeSpan.FromMinutes(31));
        Assert.False(appLock.IsLocked);
        appLock.EnterForeground();

        Assert.True(appLock.IsLocked);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Foreground_AfterTheClockWasSetBack_Locks()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);

        appLock.EnterBackground();
        clock.MoveClockOnly(TimeSpan.FromHours(-2));
        appLock.EnterForeground();

        Assert.True(appLock.IsLocked);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Background_Twice_CountsFromTheFirstTime()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);

        appLock.EnterBackground();
        clock.Advance(TimeSpan.FromMinutes(3));
        appLock.EnterBackground();
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(appLock.IsLocked);
        Assert.Equal(0, clock.ActiveTimers());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Foreground_WithoutBackground_DoesNothing()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);

        clock.Advance(TimeSpan.FromHours(1));
        appLock.EnterForeground();

        Assert.False(appLock.IsLocked);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(LocalDataProtection.KeyStore)]
    [InlineData(LocalDataProtection.None)]
    public void Lock_WithoutAPassphrase_NeverHappens(LocalDataProtection protection)
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData { Protection = protection }, clock);

        appLock.LockNow();
        appLock.EnterBackground();
        clock.Advance(TimeSpan.FromHours(1));
        appLock.EnterForeground();

        Assert.False(appLock.IsEnabled);
        Assert.False(appLock.IsLocked);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Unlock_WrongPassphrase_StaysLockedAndTheRightOneUnlocks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var localData = new LockedLocalData();
        using var appLock = new AppLock(localData, new ManualClock());
        var changes = Count(appLock);

        appLock.LockNow();
        appLock.LockNow();
        Assert.True(appLock.IsLocked);
        Assert.Equal(1, changes());

        Assert.False(await appLock.UnlockAsync("violet harbour 4471 kite", cancellationToken));
        Assert.True(appLock.IsLocked);
        Assert.True(await appLock.UnlockAsync(LockedLocalData.RightPassphrase, cancellationToken));
        Assert.False(appLock.IsLocked);
        Assert.Equal(2, changes());
        Assert.True(await appLock.UnlockAsync("anything at all", cancellationToken));
        Assert.Equal(2, localData.Checks);
    }

    [Fact]
    public void Dispose_StopsTheTimerAndLocksNoMore()
    {
        var clock = new ManualClock();
        var appLock = new AppLock(new LockedLocalData(), clock);

        appLock.EnterBackground();
        appLock.Dispose();
        clock.Advance(TimeSpan.FromHours(1));
        appLock.LockNow();

        Assert.False(appLock.IsLocked);
        Assert.Equal(0, clock.ActiveTimers());
    }

    [Fact]
    [Trait("Story", "US-21")]
    public void Reset_WhileLockedAndInTheBackground_UnlocksOnceAndStopsTheTimer()
    {
        var clock = new ManualClock();
        using var appLock = new AppLock(new LockedLocalData(), clock);
        var changes = Count(appLock);
        appLock.LockNow();
        appLock.EnterBackground();

        appLock.Reset();
        appLock.Reset();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.False(appLock.IsLocked);
        Assert.False(appLock.IsInBackground);
        Assert.Equal(2, changes());
        Assert.Equal(0, clock.ActiveTimers());
    }

    [Fact]
    public void Construct_NonPositiveLimit_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppLock(new LockedLocalData(), new ManualClock(), TimeSpan.Zero));
    }

    private static Func<int> Count(AppLock appLock)
    {
        var count = 0;
        appLock.Changed += (_, _) => count++;
        return () => count;
    }
}
