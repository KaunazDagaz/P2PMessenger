using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IActivityApplicationLifetime activityLifetime:
                activityLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
                break;

            case IClassicDesktopStyleApplicationLifetime desktopLifetime:
                desktopLifetime.MainWindow = new MainWindow { DataContext = new MainViewModel() };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
