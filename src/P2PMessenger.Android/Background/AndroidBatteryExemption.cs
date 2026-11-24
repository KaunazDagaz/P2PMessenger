using Android.Content;
using Android.OS;
using P2PMessenger.Android.Scanning;
using P2PMessenger.Application.Background;
using AndroidSettings = Android.Provider.Settings;
using AndroidUri = Android.Net.Uri;

namespace P2PMessenger.Android.Background;

internal sealed class AndroidBatteryExemption : IBatteryExemption
{
    private readonly Context context;

    public AndroidBatteryExemption(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    public bool IsExempt =>
        context.GetSystemService(Context.PowerService) is PowerManager power && power.IsIgnoringBatteryOptimizations(context.PackageName);

    public bool IsRestricted => IsBackgroundRestricted(context);

    public async Task<bool> RequestAsync(CancellationToken cancellationToken)
    {
        if (IsExempt)
        {
            return true;
        }

        if (MainActivity.Current is not { } activity)
        {
            return false;
        }

        try
        {
            using Intent intent = new(AndroidSettings.ActionRequestIgnoreBatteryOptimizations, AndroidUri.Parse(string.Concat("package:", context.PackageName)));
            _ = await ActivityResults.StartForResultAsync(activity, intent).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ActivityNotFoundException)
        {
            return IsExempt;
        }

        return IsExempt;
    }

    public async Task<bool> OpenSettingsAsync(CancellationToken cancellationToken)
    {
        if (MainActivity.Current is not { } activity)
        {
            return false;
        }

        try
        {
            using Intent intent = new(AndroidSettings.ActionApplicationDetailsSettings, AndroidUri.Parse(string.Concat("package:", context.PackageName)));
            _ = await ActivityResults.StartForResultAsync(activity, intent).WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            return false;
        }
    }

    internal static bool IsBackgroundRestricted(Context context) =>
        OperatingSystem.IsAndroidVersionAtLeast(28)
        && context.GetSystemService(Context.ActivityService) is ActivityManager activities
        && activities.IsBackgroundRestricted;
}
