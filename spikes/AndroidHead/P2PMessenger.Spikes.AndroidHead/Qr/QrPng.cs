using System.Buffers.Binary;
using System.IO.Compression;

namespace P2PMessenger.Spikes.AndroidHead.Qr;

public static class QrPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] FromLuminance(byte[] pixels, int width, int height)
    {
        using var output = new MemoryStream();
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

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, y * width, width);
            }
        }

        WriteChunk(output, "IDAT"u8, raw.ToArray());
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

        var crc = uint.MaxValue;
        crc = Update(crc, type);
        crc = Update(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
