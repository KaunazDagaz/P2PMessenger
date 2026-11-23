using ZXing;
using ZXing.Common;

namespace P2PMessenger.UI.Qr;

public sealed class QrDecoder
{
    private readonly BarcodeReaderGeneric reader;

    public QrDecoder(bool thorough)
    {
        IsThorough = thorough;
        reader = new BarcodeReaderGeneric
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

    public bool IsThorough { get; }

    public string? DecodeLuminance(byte[] plane, int rowStride, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(plane);
        if (!FitsPlane(plane.Length, rowStride, width, height))
        {
            return null;
        }

        int rows = (int)Math.Min(int.MaxValue, ((long)plane.Length + rowStride - 1) / rowStride);
        return Decode(new PlanarYUVLuminanceSource(plane, rowStride, rows, 0, 0, width, height, false));
    }

    public string? DecodeBgra(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || (long)width * height * 4 > pixels.Length)
        {
            return null;
        }

        return Decode(new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32));
    }

    internal static bool FitsPlane(int length, int rowStride, int width, int height) =>
        width > 0
        && height > 0
        && rowStride >= width
        && ((long)rowStride * (height - 1)) + width <= length;

    private string? Decode(LuminanceSource source)
    {
        Result? result = reader.Decode(source);
        return result?.Text is { Length: > 0 } text ? text : null;
    }
}
