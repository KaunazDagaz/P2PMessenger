using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.Desktop.Camera;

public static class CameraFrames
{
    public const int MaxSide = 4096;

    private const int FileHeaderLength = 14;
    private const int InfoHeaderLength = 40;

    public static bool IsJpeg(ReadOnlySpan<byte> image) => image.Length > 2 && image[0] == 0xFF && image[1] == 0xD8;

    public static bool TryConvert(ReadOnlySpan<byte> image, int rawWidth, int rawHeight, [NotNullWhen(true)] out CameraImage? frame)
    {
        frame = null;
        if (image.Length > FileHeaderLength + InfoHeaderLength && image[0] == (byte)'B' && image[1] == (byte)'M')
        {
            return TryFromBitmap(image, out frame);
        }

        if (rawWidth is > 0 and <= MaxSide && rawHeight is > 0 and <= MaxSide && image.Length == rawWidth * rawHeight * 3)
        {
            frame = FromRows(image, rawWidth, rawHeight, rawWidth * 3, 3, false);
            return true;
        }

        return false;
    }

    public static CameraImage FromBgra(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (((long)stride * (height - 1)) + ((long)width * 4) > pixels.Length)
        {
            throw new ArgumentException("The pixel buffer is too small for the size.", nameof(pixels));
        }

        return FromRows(pixels, width, height, stride, 4, false);
    }

    private static bool TryFromBitmap(ReadOnlySpan<byte> image, [NotNullWhen(true)] out CameraImage? frame)
    {
        frame = null;
        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(image[10..]);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(image[14..]);
        if (headerSize < InfoHeaderLength || FileHeaderLength + (long)headerSize > image.Length)
        {
            return false;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(image[18..]);
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(image[22..]);
        short planes = BinaryPrimitives.ReadInt16LittleEndian(image[26..]);
        short bits = BinaryPrimitives.ReadInt16LittleEndian(image[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(image[30..]);
        if (signedHeight == int.MinValue)
        {
            return false;
        }

        int height = Math.Abs(signedHeight);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide || planes != 1 || bits is not (24 or 32) || compression is not (0 or 3))
        {
            return false;
        }

        int bytesPerPixel = bits / 8;
        int stride = ((width * bits) + 31) / 32 * 4;
        if (dataOffset < FileHeaderLength + InfoHeaderLength || (long)dataOffset + ((long)stride * height) > image.Length)
        {
            return false;
        }

        frame = FromRows(image[dataOffset..], width, height, stride, bytesPerPixel, signedHeight > 0);
        return true;
    }

    private static CameraImage FromRows(ReadOnlySpan<byte> rows, int width, int height, int stride, int bytesPerPixel, bool bottomUp)
    {
        byte[] bgra = new byte[width * height * 4];
        byte[] luminance = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = rows.Slice((bottomUp ? height - 1 - y : y) * stride, width * bytesPerPixel);
            for (int x = 0; x < width; x++)
            {
                int source = x * bytesPerPixel;
                int target = ((y * width) + x) * 4;
                byte blue = row[source];
                byte green = row[source + 1];
                byte red = row[source + 2];
                bgra[target] = blue;
                bgra[target + 1] = green;
                bgra[target + 2] = red;
                bgra[target + 3] = 255;
                luminance[(y * width) + x] = Luminance.Luma(red, green, blue);
            }
        }

        return new CameraImage(width, height, bgra, luminance);
    }
}
