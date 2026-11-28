using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace P2PMessenger.UI.Controls;

public sealed class Dialog : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<Dialog, bool>(nameof(IsOpen));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<Dialog, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<double> CardMaxWidthProperty =
        AvaloniaProperty.Register<Dialog, double>(nameof(CardMaxWidth), 440);

    public static readonly StyledProperty<double> AnchorWidthProperty =
        AvaloniaProperty.Register<Dialog, double>(nameof(AnchorWidth), double.PositiveInfinity);

    public static readonly StyledProperty<bool> IsMenuProperty =
        AvaloniaProperty.Register<Dialog, bool>(nameof(IsMenu));

    public static readonly RoutedEvent<RoutedEventArgs> OpenChangedEvent =
        RoutedEvent.Register<Dialog, RoutedEventArgs>("OpenChanged", RoutingStrategies.Bubble);

    private Border? scrim;
    private Border? card;

    static Dialog()
    {
        IsOpenProperty.Changed.AddClassHandler<Dialog>(static (dialog, _) => dialog.OnOpenChanged());
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public double CardMaxWidth
    {
        get => GetValue(CardMaxWidthProperty);
        set => SetValue(CardMaxWidthProperty, value);
    }

    public double AnchorWidth
    {
        get => GetValue(AnchorWidthProperty);
        set => SetValue(AnchorWidthProperty, value);
    }

    public bool IsMenu
    {
        get => GetValue(IsMenuProperty);
        set => SetValue(IsMenuProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (scrim is not null)
        {
            scrim.PointerPressed -= OnScrimPressed;
        }

        scrim = e.NameScope.Find<Border>("PART_Scrim");
        card = e.NameScope.Find<Border>("PART_Card");
        if (scrim is not null)
        {
            scrim.PointerPressed += OnScrimPressed;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (IsOpen)
        {
            RaiseEvent(new RoutedEventArgs(OpenChangedEvent, this));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (IsOpen && e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        Cancel();
        e.Handled = true;
    }

    public bool IsShown => IsOpen && IsEffectivelyVisible;

    public void Cancel()
    {
        if (CancelCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnOpenChanged()
    {
        RaiseEvent(new RoutedEventArgs(OpenChangedEvent, this));
        Dispatcher.UIThread.Post(RestoreFocus, DispatcherPriority.Input);
    }

    private void RestoreFocus()
    {
        if (IsOpen)
        {
            card?.Focus();
            return;
        }

        if (TopLevel.GetTopLevel(this) is { } root)
        {
            root.GetVisualDescendants().OfType<Dialog>().LastOrDefault(static dialog => dialog.IsOpen && dialog.IsEffectivelyVisible)?.card?.Focus();
        }
    }
}
