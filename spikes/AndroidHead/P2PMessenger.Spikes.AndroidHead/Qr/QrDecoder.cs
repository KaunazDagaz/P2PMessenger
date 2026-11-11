using System.Diagnostics;
using ZXing;
using ZXing.Common;

namespace P2PMessenger.Spikes.AndroidHead.Qr;

public sealed class QrDecoder
{
    private readonly BarcodeReaderGeneric _reader;

    public QrDecoder(bool thorough = false)
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = thorough,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = thorough,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
    }

    public TimeSpan LastDuration { get; private set; }

    public bool TryDecodeLuminance(byte[] plane, int rowStride, int width, int height, out string text)
    {
        var source = new PlanarYUVLuminanceSource(plane, rowStride, (plane.Length + rowStride - 1) / rowStride, 0, 0, width, height, false);
        return TryDecode(source, out text);
    }

    public bool TryDecodeRgb(byte[] pixels, int width, int height, RGBLuminanceSource.BitmapFormat format, out string text)
    {
        var source = new RGBLuminanceSource(pixels, width, height, format);
        return TryDecode(source, out text);
    }

    private bool TryDecode(LuminanceSource source, out string text)
    {
        var started = Stopwatch.GetTimestamp();
        var result = _reader.Decode(source);
        LastDuration = Stopwatch.GetElapsedTime(started);
        text = result?.Text ?? string.Empty;
        return result is not null;
    }
}
