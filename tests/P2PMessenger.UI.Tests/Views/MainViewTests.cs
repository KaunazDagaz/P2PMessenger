using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class MainViewTests
{
    [AvaloniaFact]
    public void MainView_ShownWithViewModel_DisplaysTitleAndStatus()
    {
        var viewModel = new MainViewModel();
        var view = new MainView { DataContext = viewModel };
        var window = new Window { Content = view };

        window.Show();

        Assert.Equal(viewModel.Title, view.FindControl<TextBlock>("TitleText")?.Text);
        Assert.Equal(viewModel.Status, view.FindControl<TextBlock>("StatusText")?.Text);
    }
}
