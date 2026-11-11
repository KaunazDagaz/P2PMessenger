using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using P2PMessenger.Spikes.AndroidHead.Head.ViewModels;
using P2PMessenger.Spikes.AndroidHead.Head.Views;

namespace P2PMessenger.Spikes.AndroidHead.Head;

public partial class App : Avalonia.Application
{
    public SpikeServices? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime && Services is { } services)
        {
            activityLifetime.MainViewFactory = () => new SpikeView { DataContext = new SpikeViewModel(services) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
