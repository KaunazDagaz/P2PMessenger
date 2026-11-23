using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Desktop.Camera;

public sealed class CameraScanWindow : Window, IDisposable
{
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private WriteableBitmap? bitmap;
    private int pending;

    public CameraScanWindow(QrScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Title = request.Title;
        Width = 560;
        Height = 560;
        MinWidth = 320;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Hint = request.Hint;
        status.Text = request.Starting;

        Button cancel = new()
        {
            Content = request.Cancel,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12),
            IsCancel = true,
        };
        cancel.Click += (_, _) => Close();

        DockPanel root = new();
        DockPanel.SetDock(status, Dock.Top);
        DockPanel.SetDock(cancel, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(cancel);
        root.Children.Add(preview);
        Content = root;
    }

    public string Hint { get; }

    public void ShowFrame(CameraImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (Interlocked.Exchange(ref pending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Draw(frame);
            }
            finally
            {
                Volatile.Write(ref pending, 0);
            }
        });
    }

    public void Dispose()
    {
        preview.Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    private void Draw(CameraImage frame)
    {
        if (!IsVisible)
        {
            return;
        }

        status.Text = Hint;
        PixelSize size = new(frame.Width, frame.Height);
        if (bitmap is null || bitmap.PixelSize != size)
        {
            preview.Source = null;
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
            preview.Source = bitmap;
        }

        using (ILockedFramebuffer locked = bitmap.Lock())
        {
            int row = frame.Width * 4;
            for (int y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Bgra, y * row, locked.Address + (y * locked.RowBytes), row);
            }
        }

        preview.InvalidateVisual();
    }
}
