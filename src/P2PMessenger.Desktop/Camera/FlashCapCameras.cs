using FlashCap;

namespace P2PMessenger.Desktop.Camera;

public sealed class FlashCapCameras : ICameraSource
{
    private readonly Lock gate = new();
    private CaptureDeviceDescriptor[] descriptors = [];

    public IReadOnlyList<CameraEntry> List()
    {
        CaptureDeviceDescriptor[] listed = [.. new CaptureDevices().EnumerateDescriptors()];
        lock (gate)
        {
            descriptors = listed;
        }

        return [.. listed.Select(static (descriptor, index) => new CameraEntry(
            index,
            descriptor.DeviceType.ToString(),
            descriptor.Name ?? string.Empty,
            [.. descriptor.Characteristics.Select(static (format, formatIndex) => new CameraFormat(
                formatIndex,
                format.Width,
                format.Height,
                (double)format.FramesPerSecond,
                format.IsCompression,
                format.PixelFormat != PixelFormats.Unknown))]))];
    }

    public async Task<IAsyncDisposable> OpenAsync(CameraEntry entry, CameraFormat format, Action<byte[]> onFrame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(onFrame);

        CaptureDeviceDescriptor descriptor;
        lock (gate)
        {
            descriptor = descriptors[entry.Index];
        }

        VideoCharacteristics characteristics = descriptor.Characteristics[format.Index];
        CaptureDevice device = await descriptor.OpenAsync(
            characteristics,
            TranscodeFormats.Auto,
            scope => onFrame(scope.Buffer.ExtractImage()),
            cancellationToken).ConfigureAwait(false);
        try
        {
            await device.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            device.Dispose();
            throw;
        }

        return new Running(device);
    }

    private sealed class Running(CaptureDevice device) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await device.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                device.Dispose();
            }
        }
    }
}
