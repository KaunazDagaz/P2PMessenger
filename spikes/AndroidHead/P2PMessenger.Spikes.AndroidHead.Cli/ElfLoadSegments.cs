using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal sealed record ElfLoadSegments(bool Is64Bit, IReadOnlyList<long> Alignments)
{
    private const uint LoadSegment = 1;
    private const byte Class32 = 1;
    private const byte Class64 = 2;
    private const byte LittleEndian = 1;

    public static bool TryRead(ReadOnlySpan<byte> file, [NotNullWhen(true)] out ElfLoadSegments? segments)
    {
        segments = null;
        if (file.Length < 0x34 || !file[..4].SequenceEqual("\u007fELF"u8) || file[5] != LittleEndian)
        {
            return false;
        }

        if (file[4] is not (Class32 or Class64))
        {
            return false;
        }

        bool is64Bit = file[4] == Class64;
        if (is64Bit && file.Length < 0x40)
        {
            return false;
        }

        long headerOffset = is64Bit
            ? (long)BinaryPrimitives.ReadUInt64LittleEndian(file[0x20..])
            : BinaryPrimitives.ReadUInt32LittleEndian(file[0x1C..]);
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(file[(is64Bit ? 0x36 : 0x2A)..]);
        int headerCount = BinaryPrimitives.ReadUInt16LittleEndian(file[(is64Bit ? 0x38 : 0x2C)..]);
        int minimumHeaderSize = is64Bit ? 56 : 32;
        if (headerSize < minimumHeaderSize || headerOffset < 0 || headerOffset + ((long)headerSize * headerCount) > file.Length)
        {
            return false;
        }

        List<long> alignments = [];
        for (int index = 0; index < headerCount; index++)
        {
            ReadOnlySpan<byte> header = file.Slice((int)headerOffset + (index * headerSize), headerSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LoadSegment)
            {
                continue;
            }

            alignments.Add(is64Bit
                ? (long)BinaryPrimitives.ReadUInt64LittleEndian(header[48..])
                : BinaryPrimitives.ReadUInt32LittleEndian(header[28..]));
        }

        segments = new ElfLoadSegments(is64Bit, alignments);
        return alignments.Count > 0;
    }
}
