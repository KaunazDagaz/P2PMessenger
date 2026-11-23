using Android.Content;
using Android.Content.PM;

namespace P2PMessenger.Android.Scanning;

internal static class ActivityResults
{
    private const int FirstCode = 0x1000;
    private const int LastCode = 0x7FFF;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<int, TaskCompletionSource<ActivityOutcome>> Pending = [];
    private static readonly Dictionary<int, TaskCompletionSource<bool>> PendingPermissions = [];
    private static int nextCode = FirstCode;

    public static Task<ActivityOutcome> StartForResultAsync(Activity activity, Intent intent)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(intent);

        TaskCompletionSource<ActivityOutcome> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int code;
        lock (Gate)
        {
            code = NextCode();
            Pending[code] = source;
        }

        try
        {
            activity.StartActivityForResult(intent, code);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or InvalidOperationException)
        {
            lock (Gate)
            {
                _ = Pending.Remove(code);
            }

            source.TrySetException(exception);
        }

        return source.Task;
    }

    public static Task<bool> RequestPermissionAsync(Activity activity, string permission)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        TaskCompletionSource<bool> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int code;
        lock (Gate)
        {
            code = NextCode();
            PendingPermissions[code] = source;
        }

        activity.RequestPermissions([permission], code);
        return source.Task;
    }

    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<ActivityOutcome>? source;
        lock (Gate)
        {
            _ = Pending.Remove(requestCode, out source);
        }

        source?.TrySetResult(new ActivityOutcome(resultCode, data));
    }

    public static void OnPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        TaskCompletionSource<bool>? source;
        lock (Gate)
        {
            _ = PendingPermissions.Remove(requestCode, out source);
        }

        source?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }

    private static int NextCode()
    {
        int code = nextCode;
        nextCode = code >= LastCode ? FirstCode : code + 1;
        return code;
    }
}
