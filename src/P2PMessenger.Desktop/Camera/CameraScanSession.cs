using P2PMessenger.Core;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.Desktop.Camera;

public sealed class CameraScanSession : IAsyncDisposable
{
    private readonly ICameraSource cameras;
    private readonly Func<byte[], CameraImage?> decodeJpeg;
    private readonly QrFrameReader reader = new();
    private readonly TaskCompletionSource<string> found = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAsyncDisposable? device;
    private CameraFormat? format;
    private int reading;
    private int disposed;

    public CameraScanSession(ICameraSource cameras, Func<byte[], CameraImage?> decodeJpeg)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentNullException.ThrowIfNull(decodeJpeg);

        this.cameras = cameras;
        this.decodeJpeg = decodeJpeg;
    }

    public event EventHandler<CameraImage>? FrameShown;

    public Task<string> Found => found.Task;

    public long FramesRead => reader.Frames;

    public async Task<CameraOpenOutcome> StartAsync(IReadOnlyList<CameraEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (CameraEntry entry in CameraChoice.Order(entries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CameraChoice.ChooseFormat(entry.Formats) is not { } chosen)
            {
                continue;
            }

            format = chosen;
            try
            {
                device = await cameras.OpenAsync(entry, chosen, OnFrame, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (Failures.IsFailure(exception))
            {
                device = null;
                continue;
            }

            if (Volatile.Read(ref disposed) != 0)
            {
                await StopAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return CameraOpenOutcome.NoCamera;
            }

            return CameraOpenOutcome.Started;
        }

        return CameraOpenOutcome.NoCamera;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    internal void OnFrame(byte[] image)
    {
        if (found.Task.IsCompleted || Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref reading, 1) != 0)
        {
            return;
        }

        try
        {
            CameraImage? frame = Convert(image);
            if (frame is null)
            {
                return;
            }

            FrameShown?.Invoke(this, frame);
            if (reader.ReadLuminance(frame.Luminance, frame.Width, frame.Width, frame.Height) is { } text)
            {
                found.TrySetResult(text);
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    private CameraImage? Convert(byte[] image)
    {
        if (CameraFrames.IsJpeg(image))
        {
            try
            {
                return decodeJpeg(image);
            }
            catch (Exception exception) when (Failures.IsRecoverable(exception))
            {
                return null;
            }
        }

        return CameraFrames.TryConvert(image, format?.Width ?? 0, format?.Height ?? 0, out CameraImage? frame) ? frame : null;
    }

    private async Task StopAsync()
    {
        IAsyncDisposable? running = Interlocked.Exchange(ref device, null);
        if (running is null)
        {
            return;
        }

        try
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            _ = exception;
        }
    }
}
