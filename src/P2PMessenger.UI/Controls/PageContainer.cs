using Avalonia;
using Avalonia.Controls;

namespace P2PMessenger.UI.Controls;

public sealed class PageContainer : ContentControl
{
    public static readonly StyledProperty<double> ContentMaxWidthProperty =
        AvaloniaProperty.Register<PageContainer, double>(nameof(ContentMaxWidth), 640);

    public static readonly StyledProperty<object?> OverlayProperty =
        AvaloniaProperty.Register<PageContainer, object?>(nameof(Overlay));

    public double ContentMaxWidth
    {
        get => GetValue(ContentMaxWidthProperty);
        set => SetValue(ContentMaxWidthProperty, value);
    }

    public object? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }
}
