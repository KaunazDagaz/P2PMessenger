using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using P2PMessenger.Spikes.AndroidHead.Head.ViewModels;

namespace P2PMessenger.Spikes.AndroidHead.Head.Views;

public partial class SpikeView : UserControl
{
    public SpikeView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SpikeViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SpikeViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(SpikeViewModel.Log))
        {
            Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(SpikeViewModel.IsScannerVisible))
        {
            ScannerFrame.Child = viewModel.IsScannerVisible
                ? (Avalonia.Application.Current as App)?.Services?.Scanner.CreateEmbeddedView(viewModel.OnScanText)
                : null;
        }
    }
}
