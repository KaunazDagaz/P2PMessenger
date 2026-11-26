using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupJoin : SessionMessage, IEquatable<GroupJoin>
{
    public const int Length = GroupId.Length + sizeof(uint) + GroupRecord.HashLength;

    private readonly byte[] recordHash;

    private GroupJoin(GroupId groupId, int epoch, byte[] recordHash)
    {
        GroupId = groupId;
        Epoch = epoch;
        this.recordHash = recordHash;
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public ReadOnlySpan<byte> RecordHash => recordHash;

    public override SessionMessageType Type => SessionMessageType.GroupJoin;

    internal override int BodyLength => Length;

    public static GroupJoin Create(GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new GroupJoin(record.GroupId, record.Epoch, record.Hash.ToArray());
    }

    public bool Equals(GroupJoin? other) =>
        other is not null && GroupId.Equals(other.GroupId) && Epoch == other.Epoch && recordHash.AsSpan().SequenceEqual(other.recordHash);

    public override bool Equals(object? obj) => obj is GroupJoin other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GroupId, Epoch);

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupJoin? join)
    {
        join = null;
        if (body.Length != Length || !GroupId.TryCreate(body[..GroupId.Length], out GroupId? groupId))
        {
            return false;
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(body[GroupId.Length..]);
        if (epoch is 0 or > GroupRecord.MaxEpoch)
        {
            return false;
        }

        join = new GroupJoin(groupId, (int)epoch, body[(GroupId.Length + sizeof(uint))..].ToArray());
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        GroupId.Bytes.CopyTo(body);
        BinaryPrimitives.WriteUInt32BigEndian(body[GroupId.Length..], (uint)Epoch);
        recordHash.CopyTo(body[(GroupId.Length + sizeof(uint))..]);
    }
}
