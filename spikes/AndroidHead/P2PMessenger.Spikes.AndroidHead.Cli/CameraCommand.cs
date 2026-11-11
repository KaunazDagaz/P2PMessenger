using System.Diagnostics;
using System.Globalization;
using FlashCap;
using P2PMessenger.Spikes.AndroidHead.Qr;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal static class CameraCommand
{
    public static async Task<int> RunAsync(CommandLine line, CancellationToken cancellationToken)
    {
        var devices = new CaptureDevices();
        CaptureDeviceDescriptor[] descriptors;
        try
        {
            descriptors = devices.GetDescriptors();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SpikeLog.Write($"camera: enumerating capture devices failed ({exception.GetType().Name}: {exception.Message})");
            return 1;
        }

        SpikeLog.Write($"camera: {descriptors.Length} capture device(s) found by FlashCap {typeof(CaptureDevices).Assembly.GetName().Version}");
        for (var index = 0; index < descriptors.Length; index++)
        {
            var descriptor = descriptors[index];
            SpikeLog.Write($"  [{index}] {descriptor.Name} ({descriptor.DeviceType}): {descriptor.Description}, {descriptor.Characteristics.Length} format(s)");
            if (line.Flag("formats"))
            {
                foreach (var characteristics in descriptor.Characteristics)
                {
                    SpikeLog.Write($"        {characteristics}");
                }
            }
        }

        if (descriptors.Length == 0)
        {
            SpikeLog.Write("camera: no camera on this computer; US-03's text or file code is the way to add a contact here");
            return 1;
        }

        if (line.Flag("list"))
        {
            return 0;
        }

        var chosen = descriptors[Math.Min(line.Number("device", 0), descriptors.Length - 1)];
        var format = ChooseFormat(chosen.Characteristics, line.Number("width", 640));
        if (format is null)
        {
            SpikeLog.Write("camera: the device offers no usable format");
            return 1;
        }

        SpikeLog.Write($"camera: using {chosen.Name} with {format}");
        try
        {
            return await CaptureAsync(chosen, format, TimeSpan.FromSeconds(line.Number("seconds", 20)), line.Flag("any"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            SpikeLog.Write($"camera: {chosen.Name} ({chosen.DeviceType}) could not be opened or started: {exception.GetType().Name}: {exception.Message}");
            SpikeLog.Write("camera: treat this as no camera; try another entry with --device <n>, or add the contact with the text or file code (US-03)");
            return 1;
        }
    }

    private static VideoCharacteristics? ChooseFormat(VideoCharacteristics[] formats, int preferredWidth)
    {
        var usable = formats.Where(format => format.PixelFormat != PixelFormats.Unknown).ToArray();
        return usable
            .Where(format => !format.IsCompression)
            .OrderBy(format => Math.Abs(format.Width - preferredWidth))
            .ThenByDescending(format => format.FramesPerSecond)
            .FirstOrDefault()
            ?? usable
                .OrderBy(format => Math.Abs(format.Width - preferredWidth))
                .ThenByDescending(format => format.FramesPerSecond)
                .FirstOrDefault();
    }

    private static async Task<int> CaptureAsync(CaptureDeviceDescriptor descriptor, VideoCharacteristics format, TimeSpan duration, bool continueAfterFind, CancellationToken cancellationToken)
    {
        var decoder = new QrDecoder();
        var frames = 0;
        var decoded = 0;
        var unreadable = 0;
        var jpeg = 0;
        var total = TimeSpan.Zero;
        var firstFrame = TimeSpan.Zero;
        var started = Stopwatch.GetTimestamp();
        var found = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var device = await descriptor.OpenAsync(format, TranscodeFormats.Auto, scope =>
        {
            var image = scope.Buffer.ExtractImage();
            var index = Interlocked.Increment(ref frames);
            if (index == 1)
            {
                firstFrame = Stopwatch.GetElapsedTime(started);
            }

            if (CameraFrame.IsJpeg(image))
            {
                Interlocked.Increment(ref jpeg);
                return;
            }

            if (!CameraFrame.TryToLuminance(image, format.Width, format.Height, out var plane, out var width, out var height))
            {
                Interlocked.Increment(ref unreadable);
                return;
            }

            if (decoder.TryDecodeLuminance(plane, width, width, height, out var text))
            {
                total += decoder.LastDuration;
                if (Interlocked.Increment(ref decoded) == 1 || continueAfterFind)
                {
                    SpikeLog.Write(string.Create(
                        CultureInfo.InvariantCulture,
                        $"camera: QR code decoded from frame {index} in {decoder.LastDuration.TotalMilliseconds:0.0} ms: {text.Length} characters"));
                }

                if (!continueAfterFind)
                {
                    found.TrySetResult(text);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        await device.StartAsync(cancellationToken).ConfigureAwait(false);
        SpikeLog.Write(string.Create(CultureInfo.InvariantCulture, $"camera: capturing for up to {duration.TotalSeconds:0} s; hold a QR code in front of the camera"));
        try
        {
            await Task.WhenAny(found.Task, Task.Delay(duration, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            await device.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        SpikeLog.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"camera: {frames} frame(s) in {elapsed.TotalSeconds:0.0} s (first after {firstFrame.TotalMilliseconds:0} ms), {decoded} decoded, {unreadable} in a format the spike cannot read, {jpeg} JPEG frame(s) (the spike has no JPEG decoder)"));
        if (frames == 0)
        {
            SpikeLog.Write("camera: no frame arrived; the camera may be in use by another app or blocked by Windows' camera privacy setting");
            return 1;
        }

        return decoded > 0 ? 0 : 1;
    }
}
