using Android.Content;
using Android.Content.PM;
using Android.Views;
using P2PMessenger.Spikes.Tor.Cli;
using Typeface = Android.Graphics.Typeface;

namespace P2PMessenger.Spikes.Tor.Android;

[Activity(Label = "Tor spike", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    private TextView? _log;
    private ScrollView? _scroll;
    private EditText? _address;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SpikeConsole.Install();
        SetContentView(BuildLayout());
        RequestNotificationPermission();
        if (savedInstanceState is null)
        {
            SpikeRunner.PrintEnvironment(this);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        SpikeConsole.LineWritten += OnLineWritten;
        if (_log is not null)
        {
            _log.Text = SpikeConsole.Snapshot();
        }

        ScrollToEnd();
    }

    protected override void OnPause()
    {
        SpikeConsole.LineWritten -= OnLineWritten;
        base.OnPause();
    }

    private static LinearLayout.LayoutParams FullWidth() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private static void StopRunning() => SpikeRunner.Stop();

    private LinearLayout BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        var padding = Dp(8);
        root.SetPadding(padding, padding, padding, padding);

        _address = new EditText(this) { Hint = "Onion address to dial" };
        _address.SetSingleLine(true);
        root.AddView(_address, FullWidth());

        root.AddView(ButtonRow(("Self-test", RunSelfTest), ("Pair", RunPair), ("Serve", RunServe)), FullWidth());
        root.AddView(ButtonRow(("Dial", RunDial), ("Stop", StopRunning), ("Copy log", CopyLog)), FullWidth());

        _log = new TextView(this) { Typeface = Typeface.Monospace, TextSize = 11 };
        _log.SetTextIsSelectable(true);
        _scroll = new ScrollView(this);
        _scroll.AddView(_log);
        root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        return root;
    }

    private LinearLayout ButtonRow(params (string Text, Action OnClick)[] buttons)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var (text, onClick) in buttons)
        {
            var button = new Button(this) { Text = text };
            button.SetAllCaps(false);
            button.Click += (_, _) => onClick();
            row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        }

        return row;
    }

    private void RunSelfTest() => SpikeRunner.Run(this, "selftest", SelfTestCommand.RunAsync);

    private void RunPair() => SpikeRunner.Run(this, "pair", PairCommand.RunAsync);

    private void RunServe() =>
        SpikeRunner.Run(this, "serve", (options, token) => ServeCommand.RunAsync(options with { Name = "phone-serve" }, token));

    private void RunDial()
    {
        var address = _address?.Text?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            Console.WriteLine("Enter the onion address to dial first.");
            return;
        }

        SpikeRunner.Run(this, "dial", (options, token) => DialCommand.RunAsync(options with { Name = "phone-dial", Target = address }, token));
    }

    private void CopyLog()
    {
        if (GetSystemService(ClipboardService) is not ClipboardManager clipboard)
        {
            return;
        }

        clipboard.PrimaryClip = ClipData.NewPlainText("Tor spike log", SpikeConsole.Snapshot());
        Toast.MakeText(this, "Log copied", ToastLength.Short)?.Show();
    }

    private void RequestNotificationPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([global::Android.Manifest.Permission.PostNotifications], 1);
        }
    }

    private void OnLineWritten(object? sender, string line) => RunOnUiThread(() =>
    {
        _log?.Append(line + "\n");
        ScrollToEnd();
    });

    private void ScrollToEnd()
    {
        if (_scroll is null)
        {
            return;
        }

        ScrollView scroll = _scroll;
        scroll.Post(() => scroll.FullScroll(FocusSearchDirection.Down));
    }

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1f));
}
