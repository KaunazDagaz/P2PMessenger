using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupUpdate : SessionMessage, IEquatable<GroupUpdate>
{
    private readonly byte[] key;

    private GroupUpdate(byte[] key, GroupRecord record)
    {
        this.key = key;
        Record = record;
    }

    public GroupRecord Record { get; }

    public MessageId NoticeId => GroupNotices.UpdateId(Record);

    public override SessionMessageType Type => SessionMessageType.GroupUpdate;

    internal override int BodyLength => GroupKey.Length + Record.Length;

    public static GroupUpdate Create(GroupKey groupKey, GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(record.Epoch, 2);

        if (!record.Fits(groupKey))
        {
            throw new ArgumentException("The key is not the one the record names.", nameof(groupKey));
        }

        byte[] raw = new byte[GroupKey.Length];
        groupKey.Export(raw);
        return new GroupUpdate(raw, record);
    }

    public GroupKey OpenKey() =>
        GroupKey.TryImport(key, out GroupKey? groupKey) ? groupKey : throw new InvalidOperationException("The update holds no usable key.");

    public bool Equals(GroupUpdate? other) =>
        other is not null && Record.Equals(other.Record) && CryptographicOperations.FixedTimeEquals(key, other.key);

    public override bool Equals(object? obj) => obj is GroupUpdate other && Equals(other);

    public override int GetHashCode() => Record.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupUpdate? update)
    {
        update = null;
        if (body.Length <= GroupKey.Length
            || !GroupRecord.TryParse(body[GroupKey.Length..], out GroupRecord? record)
            || record.Epoch < 2
            || !GroupKey.TryImport(body[..GroupKey.Length], out GroupKey? groupKey))
        {
            return false;
        }

        using (groupKey)
        {
            if (!record.Fits(groupKey))
            {
                return false;
            }
        }

        update = new GroupUpdate(body[..GroupKey.Length].ToArray(), record);
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        key.CopyTo(body);
        Record.ToBytes().CopyTo(body[GroupKey.Length..]);
    }
}
