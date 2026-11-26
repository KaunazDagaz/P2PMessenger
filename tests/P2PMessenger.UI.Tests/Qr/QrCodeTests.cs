using System.Buffers.Binary;
using System.IO.Compression;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.UI.Tests.Qr;

public sealed class QrCodeTests
{
    [Theory]
    [Trait("Story", "US-01")]
    [InlineData(1)]
    [InlineData(64)]
    public void Encode_ContactCode_DecodesToTheSameText(int nameLength)
    {
        var text = CodeText(new string('n', nameLength));
        var matrix = QrMatrix.Encode(text);

        var pixels = matrix.ToLuminance(4, out var width);

        Assert.Equal(text, new QrDecoder(false).DecodeLuminance(pixels, width, width, width));
        Assert.InRange(matrix.Size, 21, 73);
        Assert.Equal(matrix.Size + (2 * QrMatrix.QuietZone), matrix.SizeWithQuietZone);
        Assert.Equal(matrix.SizeWithQuietZone * 4, width);
    }

    [Fact]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-15")]
    public void Encode_ManyCodes_AreReadUprightByTheFastDecoderAtEverySize()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var random = new Random(20261123);
        var decoder = new QrDecoder(false);
        for (var index = 0; index < 60; index++)
        {
            var text = "P2PM1-" + new string([.. Enumerable.Range(0, random.Next(266, 368)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            var matrix = QrMatrix.Encode(text);
            foreach (var scale in new[] { 3, 4, 6 })
            {
                var pixels = matrix.ToLuminance(scale, out var width);
                Assert.Equal(text, decoder.DecodeLuminance(pixels, width, width, width));
            }
        }
    }

    [Fact]
    public void ToLuminance_LeavesAWhiteQuietZoneAndDrawsDarkModules()
    {
        var matrix = QrMatrix.Encode("P2PM1-TEST");

        var pixels = matrix.ToLuminance(2, out var width);

        for (var x = 0; x < width; x++)
        {
            Assert.Equal(255, pixels[x]);
            Assert.Equal(255, pixels[((width - 1) * width) + x]);
        }

        Assert.True(matrix[0, 0]);
        Assert.Equal(0, pixels[(QrMatrix.QuietZone * 2 * width) + (QrMatrix.QuietZone * 2)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[matrix.Size, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix.ToLuminance(0, out _));
    }

    [Fact]
    public void DecodeLuminance_CameraPlaneWithRowPaddingAndShortLastRow_IsRead()
    {
        var text = CodeText("Bob");
        var pixels = QrMatrix.Encode(text).ToLuminance(3, out var width);
        var stride = width + 37;
        var plane = new byte[(stride * (width - 1)) + width];
        for (var y = 0; y < width; y++)
        {
            pixels.AsSpan(y * width, width).CopyTo(plane.AsSpan(y * stride));
        }

        Assert.Equal(text, new QrDecoder(false).DecodeLuminance(plane, stride, width, width));
    }

    [Theory]
    [InlineData(0, 10, 10, 100)]
    [InlineData(10, 0, 10, 100)]
    [InlineData(10, 10, 9, 100)]
    [InlineData(10, 10, 11, 100)]
    [InlineData(10, 10, 10, 99)]
    public void DecodeLuminance_PlaneThatDoesNotFit_ReturnsNull(int width, int height, int stride, int length)
    {
        Assert.Null(new QrDecoder(true).DecodeLuminance(new byte[length], stride, width, height));
    }

    [Fact]
    public void DecodeLuminance_BlankOrNoise_ReturnsNull()
    {
        var blank = new byte[320 * 240];
        Array.Fill(blank, (byte)200);
        var noise = new byte[320 * 240];
        new Random(17).NextBytes(noise);

        Assert.Null(new QrDecoder(true).DecodeLuminance(blank, 320, 320, 240));
        Assert.Null(new QrDecoder(true).DecodeLuminance(noise, 320, 320, 240));
    }

    [Fact]
    public void FrameReader_LightModulesOnDark_AreReadByTheThoroughDecoderEveryFifthFrame()
    {
        var text = CodeText("Carol");
        var pixels = QrMatrix.Encode(text).ToLuminance(3, out var width);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(255 - pixels[index]);
        }

        var reader = new QrFrameReader();
        for (var frame = 1; frame < QrFrameReader.ThoroughEvery; frame++)
        {
            Assert.Null(reader.ReadLuminance(pixels, width, width, width));
        }

        Assert.Equal(text, reader.ReadLuminance(pixels, width, width, width));
        Assert.Equal(QrFrameReader.ThoroughEvery, reader.Frames);
    }

    [Fact]
    public void FrameReader_RotatedCode_IsRead()
    {
        var text = CodeText("Dave");
        var pixels = QrMatrix.Encode(text).ToLuminance(3, out var width);
        var turned = new byte[pixels.Length];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                turned[(x * width) + (width - 1 - y)] = pixels[(y * width) + x];
            }
        }

        var reader = new QrFrameReader();
        string? decoded = null;
        for (var frame = 0; frame < QrFrameReader.ThoroughEvery && decoded is null; frame++)
        {
            decoded = reader.ReadLuminance(turned, width, width, width);
        }

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Luminance_TransparentPixels_CountAsWhite()
    {
        byte[] pixels =
        [
            0, 0, 0, 0,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 128,
        ];

        var plane = Luminance.FromBgraOverWhite(pixels, 2, 2, 8);

        Assert.Equal([255, 0, 255, 127], plane);
        Assert.Throws<ArgumentException>(() => Luminance.FromBgraOverWhite(pixels, 2, 3, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Luminance.FromBgraOverWhite(pixels, 3, 1, 8));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, false)]
    [InlineData(new byte[] { 0x50, 0x32, 0x50, 0x4D, 0x31, 0x2D }, false)]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E }, false)]
    public void ImageFiles_KnowsPictureSignatures(byte[] start, bool expected)
    {
        Assert.Equal(expected, ImageFiles.LooksLikeImage(start));
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Png_HasAValidStructureAndHoldsTheScaledCode()
    {
        var matrix = QrMatrix.Encode(CodeText("Erin"));

        var png = QrPng.Render(matrix);

        Assert.True(ImageFiles.LooksLikeImage(png));
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(static chunk => chunk.Type));
        var header = chunks[0].Data;
        var side = matrix.SizeWithQuietZone * QrPng.Scale;
        Assert.Equal(side, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(side, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 0, 0, 0, 0], header[8..]);

        using var compressed = new MemoryStream(chunks[1].Data);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var rows = raw.ToArray();
        Assert.Equal(side * (side + 1), rows.Length);
        var pixels = new byte[side * side];
        for (var y = 0; y < side; y++)
        {
            Assert.Equal(0, rows[y * (side + 1)]);
            rows.AsSpan((y * (side + 1)) + 1, side).CopyTo(pixels.AsSpan(y * side));
        }

        Assert.Equal(matrix.ToLuminance(QrPng.Scale, out _), pixels);
    }

    [Fact]
    public void Png_BadSize_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => QrPng.FromLuminance(new byte[5], 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrPng.FromLuminance([], 0, 1));
    }

    internal static string CodeText(string name)
    {
        using var keys = KeySet.Create();
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        return ContactCode.Create(keys, displayName).ToText();
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string Type, byte[] Data)>();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            Assert.Equal(Crc32(png.AsSpan(offset + 4, length + 4)), crc);
            chunks.Add((type, data));
            offset += 12 + length;
        }

        return chunks;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}
