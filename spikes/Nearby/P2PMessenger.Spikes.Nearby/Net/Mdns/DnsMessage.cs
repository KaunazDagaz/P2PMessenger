using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Net.Mdns;

public static class DnsType
{
    public const ushort A = 1;
    public const ushort Reference = 12;
    public const ushort Txt = 16;
    public const ushort Srv = 33;
    public const ushort Any = 255;
}

public sealed record DnsQuestion(string Name, ushort Type);

public sealed record DnsRecord(string Name, ushort Type, string? Target = null, int Port = 0, IPAddress? Address = null);

public sealed record DnsMessage(bool IsResponse, IReadOnlyList<DnsQuestion> Questions, IReadOnlyList<DnsRecord> Records)
{
    private const int MaxMessageLength = 9000;
    private const int MaxNameLength = 255;
    private const int MaxLabelLength = 63;
    private const int MaxPointerJumps = 16;
    private const uint DefaultTtl = 120;

    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out DnsMessage? message)
    {
        message = null;
        if (data.Length is < 12 or > MaxMessageLength)
        {
            return false;
        }

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        int questionCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        int recordCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..])
            + BinaryPrimitives.ReadUInt16BigEndian(data[8..])
            + BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
        if (questionCount > 32 || recordCount > 128)
        {
            return false;
        }

        int offset = 12;
        List<DnsQuestion> questions = [];
        for (int i = 0; i < questionCount; i++)
        {
            if (!TryReadName(data, ref offset, out string name) || offset + 4 > data.Length)
            {
                return false;
            }

            questions.Add(new DnsQuestion(name, BinaryPrimitives.ReadUInt16BigEndian(data[offset..])));
            offset += 4;
        }

        List<DnsRecord> records = [];
        for (int i = 0; i < recordCount; i++)
        {
            if (!TryReadName(data, ref offset, out string name) || offset + 10 > data.Length)
            {
                return false;
            }

            ushort type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
            offset += 10;
            if (offset + length > data.Length)
            {
                return false;
            }

            int end = offset + length;
            int position = offset;
            DnsRecord record = new(name, type);
            switch (type)
            {
                case DnsType.A when length == 4:
                    record = record with { Address = new IPAddress(data.Slice(offset, 4)) };
                    break;
                case DnsType.Reference:
                    if (!TryReadName(data, ref position, out string pointer))
                    {
                        return false;
                    }

                    record = record with { Target = pointer };
                    break;
                case DnsType.Srv when length >= 6:
                    int port = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 4)..]);
                    position = offset + 6;
                    if (!TryReadName(data, ref position, out string target))
                    {
                        return false;
                    }

                    record = record with { Target = target, Port = port };
                    break;
                default:
                    break;
            }

            records.Add(record);
            offset = end;
        }

        message = new DnsMessage((flags & 0x8000) != 0, questions, records);
        return true;
    }

    public static bool NameEquals(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public byte[] Write()
    {
        List<byte> bytes = [];
        WriteU16(bytes, 0);
        WriteU16(bytes, IsResponse ? 0x8400 : 0);
        WriteU16(bytes, Questions.Count);
        WriteU16(bytes, IsResponse ? Records.Count(record => record.Type is DnsType.Reference) : 0);
        WriteU16(bytes, 0);
        WriteU16(bytes, IsResponse ? Records.Count(record => record.Type is not DnsType.Reference) : Records.Count);
        foreach (DnsQuestion question in Questions)
        {
            WriteName(bytes, question.Name);
            WriteU16(bytes, question.Type);
            WriteU16(bytes, 1);
        }

        foreach (DnsRecord record in Records.OrderByDescending(item => item.Type == DnsType.Reference))
        {
            WriteRecord(bytes, record);
        }

        return [.. bytes];
    }

    private static void WriteRecord(List<byte> bytes, DnsRecord record)
    {
        WriteName(bytes, record.Name);
        WriteU16(bytes, record.Type);
        WriteU16(bytes, record.Type == DnsType.Reference ? 1 : 0x8001);
        WriteU16(bytes, (int)(DefaultTtl >> 16));
        WriteU16(bytes, (int)(DefaultTtl & 0xFFFF));
        List<byte> data = [];
        switch (record.Type)
        {
            case DnsType.A:
                data.AddRange(record.Address?.GetAddressBytes() ?? [0, 0, 0, 0]);
                break;
            case DnsType.Reference:
                WriteName(data, record.Target ?? string.Empty);
                break;
            case DnsType.Srv:
                WriteU16(data, 0);
                WriteU16(data, 0);
                WriteU16(data, record.Port);
                WriteName(data, record.Target ?? string.Empty);
                break;
            case DnsType.Txt:
                data.Add(0);
                break;
            default:
                break;
        }

        WriteU16(bytes, data.Count);
        bytes.AddRange(data);
    }

    private static void WriteU16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void WriteName(List<byte> bytes, string name)
    {
        foreach (string label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] encoded = Encoding.UTF8.GetBytes(label);
            if (encoded.Length > MaxLabelLength)
            {
                throw new ArgumentException("A DNS label is longer than 63 bytes.", nameof(name));
            }

            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);
    }

    private static bool TryReadName(ReadOnlySpan<byte> data, ref int offset, out string name)
    {
        StringBuilder builder = new();
        int position = offset;
        int jumps = 0;
        int? resume = null;
        name = string.Empty;
        while (true)
        {
            if (position >= data.Length)
            {
                return false;
            }

            byte length = data[position];
            if (length == 0)
            {
                position++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (position + 1 >= data.Length)
                {
                    return false;
                }

                int pointer = ((length & 0x3F) << 8) | data[position + 1];
                resume ??= position + 2;
                if (++jumps > MaxPointerJumps || pointer >= data.Length)
                {
                    return false;
                }

                position = pointer;
                continue;
            }

            if ((length & 0xC0) != 0 || position + 1 + length > data.Length)
            {
                return false;
            }

            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(Encoding.UTF8.GetString(data.Slice(position + 1, length)));
            if (builder.Length > MaxNameLength)
            {
                return false;
            }

            position += 1 + length;
        }

        name = builder.ToString();
        offset = resume ?? position;
        return true;
    }
}
