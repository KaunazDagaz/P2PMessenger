using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Camera.View;

namespace P2PMessenger.Android.Scanning;

[Activity(
    Theme = "@style/MyTheme.Scan",
    Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class ScanActivity : AppCompatActivity
{
    public const string ExtraTitle = "title";
    public const string ExtraHint = "hint";
    public const string ExtraCancel = "cancel";
    public const string ExtraText = "text";
    public const string ExtraNoCamera = "no-camera";
    public const Result ResultFailed = Result.FirstUser;

    private CameraXScanner? scanner;
    private int finished;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Title = Intent?.GetStringExtra(ExtraTitle);

        FrameLayout root = new(this);
        PreviewView preview = new(this);
        preview.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
        root.AddView(preview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        TextView hint = new(this) { Text = Intent?.GetStringExtra(ExtraHint) };
        hint.SetTextColor(global::Android.Graphics.Color.White);
        hint.SetBackgroundColor(global::Android.Graphics.Color.Argb(160, 0, 0, 0));
        hint.SetPadding(32, 32, 32, 32);
        root.AddView(hint, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top));

        Button cancel = new(this) { Text = Intent?.GetStringExtra(ExtraCancel) };
        cancel.Click += (_, _) => Complete(Result.Canceled, null, false);
        FrameLayout.LayoutParams cancelLayout = new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom | GravityFlags.CenterHorizontal)
        {
            BottomMargin = 48,
        };
        root.AddView(cancel, cancelLayout);
        SetContentView(root);

        scanner = new CameraXScanner(
            this,
            preview,
            this,
            text => RunOnUiThread(() => Complete(Result.Ok, text, false)),
            noCamera => RunOnUiThread(() => Complete(ResultFailed, null, noCamera)));
        if (CheckSelfPermission(global::Android.Manifest.Permission.Camera) == Permission.Granted)
        {
            scanner.Start();
        }
        else
        {
            Complete(ResultFailed, null, false);
        }
    }

    protected override void OnDestroy()
    {
        scanner?.Dispose();
        scanner = null;
        base.OnDestroy();
    }

    private void Complete(Result result, string? text, bool noCamera)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        using Intent data = new();
        if (text is not null)
        {
            _ = data.PutExtra(ExtraText, text);
        }

        _ = data.PutExtra(ExtraNoCamera, noCamera);
        SetResult(result, data);
        Finish();
    }
}
