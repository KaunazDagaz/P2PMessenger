using System.Buffers.Binary;
using System.Text;

namespace P2PMessenger.Storage.Protection;

public readonly record struct RecordLocation
{
    private const int RowLength = sizeof(long);

    public RecordLocation(string table, string column, long row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        Table = table;
        Column = column;
        Row = row;
    }

    public string Table { get; }

    public string Column { get; }

    public long Row { get; }

    private static ReadOnlySpan<byte> Label => "P2PM/storage/v1"u8;

    public byte[] AssociatedData()
    {
        int tableLength = Encoding.ASCII.GetByteCount(Table);
        int columnLength = Encoding.ASCII.GetByteCount(Column);
        byte[] data = new byte[Label.Length + 1 + tableLength + 1 + columnLength + 1 + RowLength];

        int offset = 0;
        Label.CopyTo(data);
        offset += Label.Length + 1;
        offset += Encoding.ASCII.GetBytes(Table, data.AsSpan(offset)) + 1;
        offset += Encoding.ASCII.GetBytes(Column, data.AsSpan(offset)) + 1;
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), Row);
        return data;
    }
}
