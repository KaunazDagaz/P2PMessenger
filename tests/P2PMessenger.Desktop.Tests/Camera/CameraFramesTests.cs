using System.Buffers.Binary;
using P2PMessenger.Desktop.Camera;

namespace P2PMessenger.Desktop.Tests.Camera;

public sealed class CameraFramesTests
{
    [Theory]
    [InlineData(24, true)]
    [InlineData(24, false)]
    [InlineData(32, true)]
    [InlineData(32, false)]
    public void TryConvert_Bitmap_GivesTopDownPixelsAndLuminance(int bits, bool bottomUp)
    {
        var bitmap = Bitmap(3, 2, bits, bottomUp, static (x, y) => ((byte)(x * 10), (byte)(y * 100), (byte)200));

        Assert.True(CameraFrames.TryConvert(bitmap, 0, 0, out var frame));

        Assert.Equal(3, frame.Width);
        Assert.Equal(2, frame.Height);
        var pixel = ((1 * 3) + 2) * 4;
        Assert.Equal([200, 100, 20, 255], frame.Bgra[pixel..(pixel + 4)]);
        Assert.Equal((byte)(((20 * 299) + (100 * 587) + (200 * 114)) / 1000), frame.Luminance[(1 * 3) + 2]);
        Assert.Equal([200, 0, 0, 255], frame.Bgra[..4]);
    }

    [Fact]
    public void TryConvert_RawRgb24_UsesTheFormatSize()
    {
        var raw = new byte[4 * 2 * 3];
        raw[((1 * 4) + 3) * 3] = 7;
        raw[(((1 * 4) + 3) * 3) + 2] = 255;

        Assert.True(CameraFrames.TryConvert(raw, 4, 2, out var frame));

        Assert.Equal([7, 0, 255, 255], frame.Bgra[(((1 * 4) + 3) * 4)..]);
        Assert.False(CameraFrames.TryConvert(raw, 5, 2, out _));
        Assert.False(CameraFrames.TryConvert(raw, 0, 0, out _));
    }

    [Theory]
    [InlineData(18, 9000)]
    [InlineData(18, 0)]
    [InlineData(22, int.MinValue)]
    [InlineData(28, 16)]
    [InlineData(30, 1)]
    [InlineData(26, 2)]
    [InlineData(10, 4)]
    [InlineData(14, 12)]
    public void TryConvert_BitmapWithAnImpossibleHeader_IsRefused(int offset, int value)
    {
        var bitmap = Bitmap(3, 2, 24, true, static (_, _) => (0, 0, 0));
        if (offset is 26 or 28)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(offset), (short)value);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(offset), value);
        }

        Assert.False(CameraFrames.TryConvert(bitmap, 3, 2, out _));
    }

    [Fact]
    public void TryConvert_TruncatedBitmap_IsRefused()
    {
        var bitmap = Bitmap(64, 64, 24, true, static (_, _) => (1, 2, 3));

        Assert.False(CameraFrames.TryConvert(bitmap.AsSpan(0, bitmap.Length - 1), 0, 0, out _));
        Assert.False(CameraFrames.TryConvert(bitmap.AsSpan(0, 40), 0, 0, out _));
    }

    [Fact]
    public void IsJpeg_LooksAtTheStartOfImageMarker()
    {
        Assert.True(CameraFrames.IsJpeg([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(CameraFrames.IsJpeg([0x42, 0x4D, 0, 0]));
        Assert.False(CameraFrames.IsJpeg([0xFF]));
    }

    [Fact]
    public void FromBgra_CopiesRowsWithPadding()
    {
        var pixels = new byte[(2 * 12) + 8];
        pixels[12] = 9;
        pixels[12 + 3] = 255;

        var frame = CameraFrames.FromBgra(pixels, 2, 2, 12);

        Assert.Equal([9, 0, 0, 255], frame.Bgra[8..12]);
        Assert.Throws<ArgumentException>(() => CameraFrames.FromBgra(pixels, 2, 4, 12));
    }

    internal static byte[] Bitmap(int width, int height, int bits, bool bottomUp, Func<int, int, (byte Red, byte Green, byte Blue)> color)
    {
        var bytesPerPixel = bits / 8;
        var stride = ((width * bits) + 31) / 32 * 4;
        var file = new byte[54 + (stride * height)];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), bottomUp ? height : -height);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), (short)bits);
        for (var y = 0; y < height; y++)
        {
            var row = 54 + ((bottomUp ? height - 1 - y : y) * stride);
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = color(x, y);
                file[row + (x * bytesPerPixel)] = blue;
                file[row + (x * bytesPerPixel) + 1] = green;
                file[row + (x * bytesPerPixel) + 2] = red;
            }
        }

        return file;
    }
}
