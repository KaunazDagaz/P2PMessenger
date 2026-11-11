using Android.Content;
using Android.Content.PM;
using Android.Provider;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class AndroidPermissions(Context context) : IPermissionService
{
    public async Task<string> RequestNotificationsAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return "POST_NOTIFICATIONS: not needed before Android 13; notifications are allowed at install";
        }

        if (context.CheckSelfPermission(DeviceState.PostNotificationsPermission) == Permission.Granted)
        {
            return "POST_NOTIFICATIONS: already granted";
        }

        var activity = ActivityHost.Current ?? throw new InvalidOperationException("The app has no visible activity to ask from.");
        var granted = await ActivityBridge.RequestPermissionAsync(activity, DeviceState.PostNotificationsPermission).ConfigureAwait(false);
        return granted
            ? "POST_NOTIFICATIONS: granted by the user"
            : "POST_NOTIFICATIONS: refused; the foreground service still runs, but its notification may be hidden";
    }

    public async Task<string> RequestCameraAsync()
    {
        if (context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) == Permission.Granted)
        {
            return "CAMERA: already granted";
        }

        var activity = ActivityHost.Current ?? throw new InvalidOperationException("The app has no visible activity to ask from.");
        var granted = await ActivityBridge.RequestPermissionAsync(activity, global::Android.Manifest.Permission.Camera).ConfigureAwait(false);
        return granted ? "CAMERA: granted by the user" : "CAMERA: refused";
    }

    public async Task<string> RequestBatteryExemptionAsync()
    {
        if (DeviceState.IsExempt(context))
        {
            return "Battery optimisation: the app is already exempt";
        }

        var activity = ActivityHost.Current ?? throw new InvalidOperationException("The app has no visible activity to ask from.");
        using var uri = global::Android.Net.Uri.Parse("package:" + context.PackageName);
        using var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, uri);
        var outcome = await ActivityBridge.StartForResultAsync(activity, intent).ConfigureAwait(false);
        var exempt = DeviceState.IsExempt(context);
        return $"Battery optimisation: the system dialog returned {outcome.Result}; the app is {(exempt ? "now exempt" : "not exempt, and keeps working as it is")}";
    }

    public string DescribeBattery() => $"Battery optimisation: {DeviceState.Basic(context)}";
}
