using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace P2PMessenger.Spikes.AndroidHead.Head;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    private SpikeServices? _services;

    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public SpikeServices Services => _services ??= AndroidServices.Create(this);

    protected override AppBuilder CreateAppBuilder() =>
        AppBuilder.Configure(() => new App { Services = Services }).UseAndroid();
}

[Activity(
    Name = "io.github.kaunazdagaz.androidheadspike.MainActivity",
    Label = "Android head spike",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SpikeConsole.Install();
        ActivityHost.Current = this;
        base.OnCreate(savedInstanceState);
        ActivityBridge.Attach(this);
        AutoRun.Handle(Intent, Services);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        AutoRun.Handle(intent, Services);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(ActivityHost.Current, this))
        {
            ActivityHost.Current = null;
        }

        base.OnDestroy();
    }

    private SpikeServices Services => ((MainApplication)(Application ?? throw new InvalidOperationException("No application."))).Services;
}
