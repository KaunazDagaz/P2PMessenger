using System.Buffers.Binary;
using System.IO.Compression;

namespace P2PMessenger.UI.Qr;

public static class QrPng
{
    public const int Scale = 12;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Render(QrMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        byte[] pixels = matrix.ToLuminance(Scale, out int width);
        return FromLuminance(pixels, width, width);
    }

    public static byte[] FromLuminance(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if ((long)width * height != pixels.Length)
        {
            throw new ArgumentException("The pixel count does not match the size.", nameof(pixels));
        }

        using MemoryStream output = new();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 0;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR"u8, header);

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, y * width, width);
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);

        uint crc = uint.MaxValue;
        crc = Update(crc, type);
        crc = Update(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
