using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal static class CameraFrame
{
    public static bool IsJpeg(ReadOnlySpan<byte> image) => image.Length > 2 && image[0] == 0xFF && image[1] == 0xD8;

    public static bool TryToLuminance(ReadOnlySpan<byte> image, int rawWidth, int rawHeight, [NotNullWhen(true)] out byte[]? plane, out int width, out int height)
    {
        plane = null;
        width = 0;
        height = 0;
        if (image.Length > 54 && image[0] == (byte)'B' && image[1] == (byte)'M')
        {
            return TryFromBitmap(image, out plane, out width, out height);
        }

        if (rawWidth > 0 && rawHeight > 0 && image.Length == rawWidth * rawHeight * 3)
        {
            width = rawWidth;
            height = rawHeight;
            plane = new byte[width * height];
            for (var index = 0; index < plane.Length; index++)
            {
                plane[index] = Luma(image[(index * 3) + 2], image[(index * 3) + 1], image[index * 3]);
            }

            return true;
        }

        return false;
    }

    public static byte[] ToBitmap24(byte[] luminance, int width, int height)
    {
        var stride = ((width * 3) + 3) & ~3;
        var dataSize = stride * height;
        var file = new byte[54 + dataSize];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(34), dataSize);
        for (var y = 0; y < height; y++)
        {
            var row = 54 + ((height - 1 - y) * stride);
            for (var x = 0; x < width; x++)
            {
                var value = luminance[(y * width) + x];
                file[row + (x * 3)] = value;
                file[row + (x * 3) + 1] = value;
                file[row + (x * 3) + 2] = value;
            }
        }

        return file;
    }

    private static bool TryFromBitmap(ReadOnlySpan<byte> image, [NotNullWhen(true)] out byte[]? plane, out int width, out int height)
    {
        plane = null;
        width = 0;
        height = 0;
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(image[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(image[14..]);
        if (headerSize < 40)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(image[18..]);
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(image[22..]);
        var bits = BinaryPrimitives.ReadInt16LittleEndian(image[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(image[30..]);
        height = Math.Abs(signedHeight);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || bits is not (24 or 32) || compression is not (0 or 3))
        {
            return false;
        }

        var bytesPerPixel = bits / 8;
        var stride = ((width * bits) + 31) / 32 * 4;
        if (dataOffset < 0 || (long)dataOffset + ((long)stride * height) > image.Length)
        {
            return false;
        }

        plane = new byte[width * height];
        var bottomUp = signedHeight > 0;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = dataOffset + ((bottomUp ? height - 1 - y : y) * stride);
            for (var x = 0; x < width; x++)
            {
                var pixel = sourceRow + (x * bytesPerPixel);
                plane[(y * width) + x] = Luma(image[pixel + 2], image[pixel + 1], image[pixel]);
            }
        }

        return true;
    }

    private static byte Luma(byte red, byte green, byte blue) => (byte)(((red * 299) + (green * 587) + (blue * 114)) / 1000);
}
