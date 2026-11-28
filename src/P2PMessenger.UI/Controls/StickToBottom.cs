using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace P2PMessenger.UI.Controls;

public static class StickToBottom
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(StickToBottom));

    private static readonly AttachedProperty<bool> AtBottomProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("AtBottom", typeof(StickToBottom), defaultValue: true);

    static StickToBottom()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(static (viewer, change) =>
        {
            viewer.ScrollChanged -= OnScrollChanged;
            if (change.NewValue is true)
            {
                viewer.ScrollChanged += OnScrollChanged;
            }
        });
    }

    public static bool GetIsEnabled(ScrollViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return viewer.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(ScrollViewer viewer, bool value)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        viewer.SetValue(IsEnabledProperty, value);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || !ReferenceEquals(e.Source, viewer))
        {
            return;
        }

        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (viewer.GetValue(AtBottomProperty))
            {
                Dispatcher.UIThread.Post(viewer.ScrollToEnd, DispatcherPriority.Loaded);
            }

            return;
        }

        viewer.SetValue(AtBottomProperty, viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - 1);
    }
}
