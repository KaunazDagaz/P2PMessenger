using Android.Content;
using Android.Content.PM;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Android.Scanning;

internal sealed class AndroidQrScanner : IQrScanner
{
    public async Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        MainActivity? activity = MainActivity.Current;
        if (activity is null)
        {
            return QrScanResult.Failed;
        }

        if (activity.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) != true)
        {
            return QrScanResult.NoCamera;
        }

        if (activity.CheckSelfPermission(global::Android.Manifest.Permission.Camera) != Permission.Granted
            && !await ActivityResults.RequestPermissionAsync(activity, global::Android.Manifest.Permission.Camera))
        {
            return QrScanResult.CameraRefused;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using Intent intent = new(activity, typeof(ScanActivity));
        _ = intent.PutExtra(ScanActivity.ExtraTitle, request.Title);
        _ = intent.PutExtra(ScanActivity.ExtraHint, request.Hint);
        _ = intent.PutExtra(ScanActivity.ExtraCancel, request.Cancel);
        ActivityOutcome outcome = await ActivityResults.StartForResultAsync(activity, intent);
        string? text = outcome.Data?.GetStringExtra(ScanActivity.ExtraText);
        return outcome.Result switch
        {
            Result.Ok when !string.IsNullOrEmpty(text) => QrScanResult.Read(text),
            ScanActivity.ResultFailed when outcome.Data?.GetBooleanExtra(ScanActivity.ExtraNoCamera, false) == true => QrScanResult.NoCamera,
            ScanActivity.ResultFailed => QrScanResult.Failed,
            _ => QrScanResult.Cancelled,
        };
    }
}
