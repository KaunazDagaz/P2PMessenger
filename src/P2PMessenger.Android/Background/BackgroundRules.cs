using System.Buffers.Binary;
using System.Globalization;

namespace P2PMessenger.Android.Background;

public static class BackgroundRules
{
    public const string BootCompleted = "android.intent.action.BOOT_COMPLETED";
    public const string PackageReplaced = "android.intent.action.MY_PACKAGE_REPLACED";
    public const int ServiceNotificationId = 1;
    public const int UnlockNotificationId = 2;
    public const int LockScreenNoticeId = 3;
    public const string ScreenOff = "android.intent.action.SCREEN_OFF";
    public const string UserPresent = "android.intent.action.USER_PRESENT";
    public const int FirstMessageNotificationId = 1000;

    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromMinutes(1);

    public static TimeSpan EndProcessDelay { get; } = TimeSpan.FromMilliseconds(700);

    public static bool ShouldResume(string? action, bool wanted) =>
        wanted && action is BootCompleted or PackageReplaced;

    public static LockScreenNotice NoticeFor(string? action, IEnumerable<int> shownIds)
    {
        ArgumentNullException.ThrowIfNull(shownIds);

        return action switch
        {
            ScreenOff when shownIds.Any(id => id >= FirstMessageNotificationId) => LockScreenNotice.Show,
            UserPresent => LockScreenNotice.Remove,
            _ => LockScreenNotice.Keep,
        };
    }

    public static string StartLine(string? action, bool? foreground, bool restricted) =>
        string.Concat(
            "service: start command ",
            action ?? "(restart by the system)",
            ", foreground ",
            foreground switch { true => "yes", false => "no", null => "not reported" },
            ", background restricted ",
            restricted ? "yes" : "no");

    public static int MessageNotificationId(ReadOnlySpan<byte> contactKey)
    {
        if (contactKey.Length < sizeof(int))
        {
            throw new ArgumentException("A contact key has at least four bytes.", nameof(contactKey));
        }

        int value = BinaryPrimitives.ReadInt32BigEndian(contactKey) & int.MaxValue;
        return FirstMessageNotificationId + (value % (int.MaxValue - FirstMessageNotificationId));
    }

    public static string Heartbeat(HeartbeatReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"heartbeat #{reading.Number}: timer {reading.Lateness.TotalSeconds:+0.0;-0.0;0.0} s late, processor asleep {reading.Asleep.TotalSeconds:0.0} s, idle {Flag(reading.DeviceIdle)}, screen {Flag(reading.Interactive)}, power saver {Flag(reading.PowerSave)}, battery exempt {Flag(reading.Exempt)}, restricted {Flag(reading.Restricted)}, battery {reading.BatteryPercent} %, charging {Flag(reading.Charging)}");
    }

    private static string Flag(bool value) => value ? "yes" : "no";
}
