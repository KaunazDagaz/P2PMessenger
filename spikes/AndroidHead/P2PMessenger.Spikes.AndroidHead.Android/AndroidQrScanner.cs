using Android.Content;
using Avalonia.Controls;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class AndroidQrScanner(Context context, IPermissionService permissions) : IQrScanner
{
    public async Task<string> ScanInActivityAsync()
    {
        var activity = ActivityHost.Current ?? throw new InvalidOperationException("The app has no visible activity to start the scanner from.");
        if (context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) != Android.Content.PM.Permission.Granted)
        {
            SpikeLog.Write(await permissions.RequestCameraAsync().ConfigureAwait(false));
        }

        using var intent = new Intent(activity, typeof(ScanActivity));
        var outcome = await ActivityBridge.StartForResultAsync(activity, intent).ConfigureAwait(false);
        var text = outcome.Data?.GetStringExtra(ScanActivity.ExtraText);
        return outcome.Result == Result.Ok && text is not null
            ? $"Scan activity returned {text.Length} characters: {text}"
            : $"Scan activity ended with {outcome.Result} and no text";
    }

    public Control CreateEmbeddedView(Action<string> onText) => new CameraPreviewHost(onText);
}
