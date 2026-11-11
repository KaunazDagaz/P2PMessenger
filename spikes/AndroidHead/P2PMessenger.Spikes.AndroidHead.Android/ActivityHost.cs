using Android.Content;
using Android.Content.PM;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal static class ActivityHost
{
    public static MainActivity? Current { get; set; }
}

internal static class ActivityBridge
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<int, TaskCompletionSource<ActivityOutcome>> Pending = [];
    private static readonly Dictionary<int, TaskCompletionSource<bool>> PendingPermissions = [];
    private static int s_nextCode = 100;

    public static void Attach(MainActivity activity)
    {
        activity.ActivityResult = OnActivityResult;
        activity.RequestPermissionsResult = OnPermissionsResult;
    }

    public static Task<ActivityOutcome> StartForResultAsync(Activity activity, Intent intent)
    {
        var code = Interlocked.Increment(ref s_nextCode);
        var source = new TaskCompletionSource<ActivityOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            Pending[code] = source;
        }

        activity.StartActivityForResult(intent, code);
        return source.Task;
    }

    public static Task<bool> RequestPermissionAsync(Activity activity, string permission)
    {
        var code = Interlocked.Increment(ref s_nextCode);
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            PendingPermissions[code] = source;
        }

        activity.RequestPermissions([permission], code);
        return source.Task;
    }

    private static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<ActivityOutcome>? source;
        lock (Gate)
        {
            _ = Pending.Remove(requestCode, out source);
        }

        source?.TrySetResult(new ActivityOutcome(resultCode, data));
    }

    private static void OnPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        TaskCompletionSource<bool>? source;
        lock (Gate)
        {
            _ = PendingPermissions.Remove(requestCode, out source);
        }

        source?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
}

internal readonly record struct ActivityOutcome(Result Result, Intent? Data);
