using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Camera.View;

namespace P2PMessenger.Spikes.AndroidHead.Head;

[Activity(
    Label = "Scan a QR code",
    Theme = "@style/MyTheme.NoActionBar",
    Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class ScanActivity : AppCompatActivity
{
    public const string ExtraText = "text";

    private CameraScanSession? _session;
    private PreviewView? _preview;
    private bool _finished;

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            _session?.Start();
        }
        else
        {
            SpikeLog.Write("scan activity: the camera permission was refused");
            Finish(Result.Canceled, null);
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var root = new FrameLayout(this);
        _preview = new PreviewView(this);
        _preview.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
        root.AddView(_preview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var hint = new TextView(this) { Text = "Point the camera at a QR code" };
        hint.SetTextColor(global::Android.Graphics.Color.White);
        hint.SetBackgroundColor(global::Android.Graphics.Color.Argb(160, 0, 0, 0));
        hint.SetPadding(24, 24, 24, 24);
        root.AddView(hint, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top));

        var cancel = new Button(this) { Text = "Cancel" };
        cancel.Click += (_, _) => Finish(Result.Canceled, null);
        root.AddView(cancel, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom | GravityFlags.CenterHorizontal));
        SetContentView(root);

        _session = new CameraScanSession(this, _preview, this, text => RunOnUiThread(() => Finish(Result.Ok, text)));
        if (CheckSelfPermission(global::Android.Manifest.Permission.Camera) == Permission.Granted)
        {
            _session.Start();
        }
        else
        {
            RequestPermissions([global::Android.Manifest.Permission.Camera], 7);
        }
    }

    protected override void OnDestroy()
    {
        _session?.Dispose();
        _session = null;
        base.OnDestroy();
    }

    private void Finish(Result result, string? text)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        using var data = new Intent();
        if (text is not null)
        {
            _ = data.PutExtra(ExtraText, text);
        }

        SetResult(result, data);
        Finish();
    }
}
