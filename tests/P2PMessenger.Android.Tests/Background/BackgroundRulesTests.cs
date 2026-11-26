using P2PMessenger.Android.Background;

namespace P2PMessenger.Android.Tests.Background;

public sealed class BackgroundRulesTests
{
    [Theory]
    [Trait("Story", "US-18")]
    [InlineData("android.intent.action.BOOT_COMPLETED", true, true)]
    [InlineData("android.intent.action.MY_PACKAGE_REPLACED", true, true)]
    [InlineData("android.intent.action.BOOT_COMPLETED", false, false)]
    [InlineData("android.intent.action.MY_PACKAGE_REPLACED", false, false)]
    [InlineData("android.intent.action.LOCKED_BOOT_COMPLETED", true, false)]
    [InlineData("android.intent.action.PACKAGE_REPLACED", true, false)]
    [InlineData("io.github.kaunazdagaz.p2pmessenger.action.RESUME", true, false)]
    [InlineData("", true, false)]
    [InlineData(null, true, false)]
    public void ShouldResume_ActionAndWish_ResumesOnlyAfterBootOrUpdateWhenItWasRunning(string? action, bool wanted, bool expected) =>
        Assert.Equal(expected, BackgroundRules.ShouldResume(action, wanted));

    [Fact]
    [Trait("Story", "US-19")]
    public void MessageNotificationId_SameKey_IsTheSameAndAboveTheFixedIds()
    {
        byte[] key = new byte[32];
        key[0] = 0xFF;
        key[1] = 0xFF;
        key[2] = 0xFF;
        key[3] = 0xFF;
        byte[] other = new byte[32];
        other[3] = 1;

        int id = BackgroundRules.MessageNotificationId(key);

        Assert.Equal(id, BackgroundRules.MessageNotificationId(key));
        Assert.InRange(id, BackgroundRules.FirstMessageNotificationId, int.MaxValue);
        Assert.InRange(BackgroundRules.MessageNotificationId(new byte[32]), BackgroundRules.FirstMessageNotificationId, int.MaxValue);
        Assert.NotEqual(BackgroundRules.MessageNotificationId(new byte[32]), BackgroundRules.MessageNotificationId(other));
        Assert.NotEqual(BackgroundRules.ServiceNotificationId, BackgroundRules.UnlockNotificationId);
        Assert.True(BackgroundRules.UnlockNotificationId < BackgroundRules.FirstMessageNotificationId);
    }

    [Fact]
    [Trait("Story", "US-19")]
    public void MessageNotificationId_SixteenByteGroupId_IsAboveTheFixedIdsAndCountsForTheLockScreenNotice()
    {
        byte[] groupId = [0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

        int id = BackgroundRules.MessageNotificationId(groupId);

        Assert.Equal(id, BackgroundRules.MessageNotificationId(groupId));
        Assert.InRange(id, BackgroundRules.FirstMessageNotificationId, int.MaxValue);
        Assert.Equal(LockScreenNotice.Show, BackgroundRules.NoticeFor(BackgroundRules.ScreenOff, [id]));
    }

    [Fact]
    [Trait("Story", "US-19")]
    public void NoticeFor_ScreenOffWithAMessageShown_ShowsTheNoticeWithoutTheSender() =>
        Assert.Equal(LockScreenNotice.Show, BackgroundRules.NoticeFor("android.intent.action.SCREEN_OFF", [BackgroundRules.ServiceNotificationId, 123456789]));

    [Theory]
    [Trait("Story", "US-19")]
    [InlineData(new int[0])]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 1, 2, 3 })]
    [InlineData(new[] { 999 })]
    public void NoticeFor_ScreenOffWithoutMessages_KeepsTheLockScreenEmpty(int[] shownIds) =>
        Assert.Equal(LockScreenNotice.Keep, BackgroundRules.NoticeFor("android.intent.action.SCREEN_OFF", shownIds));

    [Theory]
    [Trait("Story", "US-19")]
    [InlineData(new int[0])]
    [InlineData(new[] { 1000 })]
    public void NoticeFor_Unlocked_RemovesTheNotice(int[] shownIds) =>
        Assert.Equal(LockScreenNotice.Remove, BackgroundRules.NoticeFor("android.intent.action.USER_PRESENT", shownIds));

    [Theory]
    [InlineData("android.intent.action.SCREEN_ON")]
    [InlineData("android.intent.action.BOOT_COMPLETED")]
    [InlineData("")]
    [InlineData(null)]
    public void NoticeFor_OtherAction_KeepsWhatIsShown(string? action) =>
        Assert.Equal(LockScreenNotice.Keep, BackgroundRules.NoticeFor(action, [1000]));

    [Fact]
    public void NoticeFor_NoList_Throws() =>
        Assert.Throws<ArgumentNullException>(() => BackgroundRules.NoticeFor(BackgroundRules.ScreenOff, null!));

    [Fact]
    public void LockScreenNoticeId_IsBelowTheMessageIdsAndUnique()
    {
        Assert.True(BackgroundRules.LockScreenNoticeId < BackgroundRules.FirstMessageNotificationId);
        Assert.Equal(3, new[] { BackgroundRules.ServiceNotificationId, BackgroundRules.UnlockNotificationId, BackgroundRules.LockScreenNoticeId }.Distinct().Count());
    }

    [Fact]
    public void MessageNotificationId_ShortKey_Throws() =>
        Assert.Throws<ArgumentException>(() => BackgroundRules.MessageNotificationId(new byte[3]));

    [Fact]
    [Trait("NFR", "NFR-05")]
    public void Heartbeat_Reading_IsOneLineWithoutAddressesOrNames()
    {
        var reading = new HeartbeatReading(7, TimeSpan.FromSeconds(12.25), TimeSpan.FromSeconds(48), true, false, false, true, false, 81, false);

        string line = BackgroundRules.Heartbeat(reading);

        Assert.Equal(
            "heartbeat #7: timer +12.3 s late, processor asleep 48.0 s, idle yes, screen no, power saver no, battery exempt yes, restricted no, battery 81 %, charging no",
            line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void Heartbeat_EarlyTimer_ShowsAMinus() =>
        Assert.StartsWith(
            "heartbeat #1: timer -0.5 s late",
            BackgroundRules.Heartbeat(new HeartbeatReading(1, TimeSpan.FromSeconds(-0.5), TimeSpan.Zero, false, true, false, false, true, 100, true)),
            StringComparison.Ordinal);

    [Theory]
    [Trait("Story", "US-18")]
    [InlineData("io.github.kaunazdagaz.p2pmessenger.action.RESUME", true, false, "service: start command io.github.kaunazdagaz.p2pmessenger.action.RESUME, foreground yes, background restricted no")]
    [InlineData("io.github.kaunazdagaz.p2pmessenger.action.RUN", false, true, "service: start command io.github.kaunazdagaz.p2pmessenger.action.RUN, foreground no, background restricted yes")]
    [InlineData(null, null, false, "service: start command (restart by the system), foreground not reported, background restricted no")]
    public void StartLine_StateOfTheService_SaysWhetherAndroidKeptItInTheForeground(string? action, bool? foreground, bool restricted, string expected) =>
        Assert.Equal(expected, BackgroundRules.StartLine(action, foreground, restricted));

    [Fact]
    public void Intervals_AreTheMeasuredOnes()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), BackgroundRules.HeartbeatInterval);
        Assert.True(BackgroundRules.EndProcessDelay < TimeSpan.FromSeconds(1));
    }
}
