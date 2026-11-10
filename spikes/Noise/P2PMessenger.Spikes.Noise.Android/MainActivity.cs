using Android.Content;
using Android.Views;
using Typeface = Android.Graphics.Typeface;

namespace P2PMessenger.Spikes.Noise.Android;

[Activity(Label = "Noise spike", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    private TextView? _log;
    private ScrollView? _scroll;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SpikeConsole.Install();
        SetContentView(BuildLayout());
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

    private LinearLayout BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        var padding = Dp(8);
        root.SetPadding(padding, padding, padding, padding);

        root.AddView(ButtonRow(("Self-test", RunSelfTest), ("Benchmark", RunBenchmark), ("Copy log", CopyLog)), FullWidth());

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

    private void RunSelfTest() => SpikeRunner.Run(this, "selftest", SelfTest.Run);

    private void RunBenchmark() => SpikeRunner.Run(this, "bench", () => Benchmark.Run());

    private void CopyLog()
    {
        if (GetSystemService(ClipboardService) is not ClipboardManager clipboard)
        {
            return;
        }

        clipboard.PrimaryClip = ClipData.NewPlainText("Noise spike log", SpikeConsole.Snapshot());
        Toast.MakeText(this, "Log copied", ToastLength.Short)?.Show();
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
