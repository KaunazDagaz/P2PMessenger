using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Desktop.Camera;

public sealed class DesktopQrScanner : IQrScanner
{
    private readonly ICameraSource cameras;

    public DesktopQrScanner()
        : this(new FlashCapCameras())
    {
    }

    public DesktopQrScanner(ICameraSource cameras)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        this.cameras = cameras;
    }

    public async Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<CameraEntry> entries;
        try
        {
            entries = await Task.Run(cameras.List, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return QrScanResult.NoCamera;
        }

        if (CameraChoice.Order(entries).Count == 0)
        {
            return QrScanResult.NoCamera;
        }

        using CameraScanWindow window = new(request);
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        await using CameraScanSession session = new(cameras, JpegFrames.Decode);
        session.FrameShown += (_, frame) => window.ShowFrame(frame);
        Show(window);
        try
        {
            CameraOpenOutcome opened = await Task.Run(() => session.StartAsync(entries, cancellationToken), cancellationToken);
            if (opened == CameraOpenOutcome.NoCamera)
            {
                return closed.Task.IsCompleted ? QrScanResult.Cancelled : QrScanResult.NoCamera;
            }

            Task cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
            Task first = await Task.WhenAny(session.Found, closed.Task, cancelled);
            return first == session.Found ? QrScanResult.Read(await session.Found) : QrScanResult.Cancelled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QrScanResult.Cancelled;
        }
        finally
        {
            await session.DisposeAsync();
            if (!closed.Task.IsCompleted)
            {
                window.Close();
            }
        }
    }

    private static void Show(Window window)
    {
        if (Dispatcher.UIThread.CheckAccess()
            && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } owner })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
