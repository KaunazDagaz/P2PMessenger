namespace P2PMessenger.UI.Qr;

public static class Luminance
{
    public static byte[] FromBgraOverWhite(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (((long)stride * (height - 1)) + ((long)width * 4) > pixels.Length)
        {
            throw new ArgumentException("The pixel buffer is too small for the size.", nameof(pixels));
        }

        byte[] plane = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++)
            {
                int pixel = x * 4;
                int luma = Luma(row[pixel + 2], row[pixel + 1], row[pixel]);
                int alpha = row[pixel + 3];
                plane[(y * width) + x] = (byte)(((luma * alpha) + (255 * (255 - alpha)) + 127) / 255);
            }
        }

        return plane;
    }

    public static byte Luma(byte red, byte green, byte blue) => (byte)(((red * 299) + (green * 587) + (blue * 114)) / 1000);
}
