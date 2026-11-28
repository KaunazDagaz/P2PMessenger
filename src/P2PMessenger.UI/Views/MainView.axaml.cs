using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P2PMessenger.UI.Controls;
using P2PMessenger.UI.ViewModels;

namespace P2PMessenger.UI.Views;

public partial class MainView : UserControl
{
    public const double CompactWidth = 560;
    public const double WideWidth = 900;

    private INotifyPropertyChanged? shell;
    private TopLevel? topLevel;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        AddHandler(Dialog.OpenChangedEvent, OnDialogOpenChanged);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (shell is not null)
        {
            shell.PropertyChanged -= OnShellChanged;
        }

        shell = DataContext as INotifyPropertyChanged;
        if (shell is not null)
        {
            shell.PropertyChanged += OnShellChanged;
        }

        ApplyWidth(Bounds.Width);
        ApplyKeyboardInset();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        topLevel.BackRequested += OnBackRequested;
        if (topLevel.InputPane is { } pane)
        {
            pane.StateChanged += OnInputPaneChanged;
            SetKeyboardInset(pane.State == InputPaneState.Open ? pane.OccludedRect.Height : 0);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (topLevel is not null)
        {
            topLevel.BackRequested -= OnBackRequested;
            if (topLevel.InputPane is { } pane)
            {
                pane.StateChanged -= OnInputPaneChanged;
            }

            topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyWidth(e.NewSize.Width);

    private void ApplyWidth(double width)
    {
        Classes.Set("compact", width < CompactWidth);
        if (DataContext is MainViewModel main && width > 0)
        {
            main.IsWide = width >= WideWidth;
        }
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (this.GetVisualDescendants().OfType<Dialog>().LastOrDefault(static dialog => dialog.IsShown) is { } dialog)
        {
            dialog.Cancel();
            e.Handled = true;
            return;
        }

        if (DataContext is MainViewModel main && main.TryGoBack())
        {
            e.Handled = true;
        }
    }

    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e) =>
        SetKeyboardInset(e.NewState == InputPaneState.Open ? e.EndRect.Height : 0);

    private void SetKeyboardInset(double inset)
    {
        if (DataContext is MainViewModel main)
        {
            main.KeyboardInset = Math.Max(0, inset);
        }
    }

    private void ApplyKeyboardInset() =>
        Root.Margin = new Thickness(0, 0, 0, DataContext is MainViewModel main ? main.KeyboardInset : 0);

    private void OnDialogOpenChanged(object? sender, RoutedEventArgs e) => UpdateDialogState();

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.KeyboardInset))
        {
            ApplyKeyboardInset();
        }

        Dispatcher.UIThread.Post(UpdateDialogState, DispatcherPriority.Loaded);
    }

    private void UpdateDialogState()
    {
        List<Dialog> open = [.. this.GetVisualDescendants().OfType<Dialog>().Where(static dialog => dialog.IsShown && !dialog.IsMenu)];
        Classes.Set("dialogOpen", open.Count > 0);
        MasterShade.IsVisible = open.Any(dialog => !MasterPane.IsVisualAncestorOf(dialog));
        DetailShade.IsVisible = open.Any(MasterPane.IsVisualAncestorOf);
    }
}
