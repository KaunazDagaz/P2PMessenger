using System.Globalization;
using System.Net;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using P2PMessenger.Spikes.Nearby.Identification;
using P2PMessenger.Spikes.Nearby.Net;
using Build = Android.OS.Build;
using Typeface = Android.Graphics.Typeface;

namespace P2PMessenger.Spikes.Nearby.Android;

[Activity(Label = "Nearby spike", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    private TextView? _log;
    private ScrollView? _scroll;
    private EditText? _name;
    private EditText? _peer;
    private EditText? _seconds;
    private EditText? _pings;
    private CheckBox? _multicastLock;
    private CheckBox? _nsd;

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

        _name = new EditText(this) { Hint = "Name of this device", Text = ProbeMessage.SanitizeName(Build.Model ?? "phone") };
        _name.SetSingleLine(true);
        root.AddView(_name, FullWidth());

        _peer = new EditText(this) { Hint = "Peer IP to dial (optional)" };
        _peer.SetSingleLine(true);
        root.AddView(_peer, FullWidth());

        _seconds = new EditText(this) { Hint = "Seconds to run (0 = until Stop)", Text = "150", InputType = global::Android.Text.InputTypes.ClassNumber };
        _seconds.SetSingleLine(true);
        root.AddView(_seconds, FullWidth());

        _pings = new EditText(this) { Hint = "Round trips per TCP test (100 for the NFR-01 look)", Text = "100", InputType = global::Android.Text.InputTypes.ClassNumber };
        _pings.SetSingleLine(true);
        root.AddView(_pings, FullWidth());

        var options = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _multicastLock = new CheckBox(this) { Text = "MulticastLock", Checked = true };
        _nsd = new CheckBox(this) { Text = "NsdManager", Checked = true };
        options.AddView(_multicastLock, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        options.AddView(_nsd, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        root.AddView(options, FullWidth());

        root.AddView(ButtonRow(("Start node", StartNode), ("Stop", StopRunning), ("Copy log", CopyLog)), FullWidth());
        root.AddView(ButtonRow(("Interfaces", ShowInterfaces), ("Self-test", RunSelfTest), ("Bench", RunBenchmark)), FullWidth());

        _log = new TextView(this) { Typeface = Typeface.Monospace, TextSize = 10 };
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

    private void StartNode()
    {
        var name = ProbeMessage.SanitizeName(_name?.Text ?? "phone");
        var seconds = int.TryParse(_seconds?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 150;
        var pings = int.TryParse(_pings?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var pingCount) && pingCount is > 0 and <= 1000 ? pingCount : 100;
        var peerText = _peer?.Text?.Trim() ?? string.Empty;
        IReadOnlyList<IPAddress> peers = IPAddress.TryParse(peerText, out var peer) ? [peer] : [];
        var useLock = _multicastLock?.Checked ?? true;
        var useNsd = _nsd?.Checked ?? true;
        var context = ApplicationContext ?? this;
        SpikeRunner.Run(this, "node", async (folder, token) =>
        {
            var options = new NodeOptions
            {
                Name = name,
                Duration = TimeSpan.FromSeconds(seconds),
                ManualPeers = peers,
                Pings = pings,
                ResultsFolder = folder,
            };
            using var node = new ProbeNode(options, new AndroidHooks(context, useLock, useNsd));
            return await node.RunAsync(token).ConfigureAwait(false);
        });
    }

    private void ShowInterfaces() => SpikeRunner.PrintEnvironment(this);

    private void RunSelfTest() => SpikeRunner.Run(this, "selftest", (_, token) => SelfTest.RunAsync(token));

    private void RunBenchmark() => SpikeRunner.Run(this, "bench", (_, _) => Task.Run(() => CallerBenchmark.Run()));

    private void CopyLog()
    {
        if (GetSystemService(ClipboardService) is not ClipboardManager clipboard)
        {
            return;
        }

        clipboard.PrimaryClip = ClipData.NewPlainText("Nearby spike log", SpikeConsole.Snapshot());
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
