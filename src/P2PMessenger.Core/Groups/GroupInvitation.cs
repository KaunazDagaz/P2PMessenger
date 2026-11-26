using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupInvitation : SessionMessage, IEquatable<GroupInvitation>
{
    private readonly byte[] key;

    private GroupInvitation(byte[] key, GroupRecord record)
    {
        this.key = key;
        Record = record;
    }

    public GroupRecord Record { get; }

    public MessageId NoticeId => GroupNotices.InvitationId(Record);

    public override SessionMessageType Type => SessionMessageType.GroupInvitation;

    internal override int BodyLength => GroupKey.Length + Record.Length;

    public static GroupInvitation Create(GroupKey groupKey, GroupRecord record)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(record);

        if (!record.Fits(groupKey))
        {
            throw new ArgumentException("The key is not the one the record names.", nameof(groupKey));
        }

        byte[] raw = new byte[GroupKey.Length];
        groupKey.Export(raw);
        return new GroupInvitation(raw, record);
    }

    public GroupKey OpenKey() =>
        GroupKey.TryImport(key, out GroupKey? groupKey) ? groupKey : throw new InvalidOperationException("The invitation holds no usable key.");

    public bool Equals(GroupInvitation? other) =>
        other is not null && Record.Equals(other.Record) && CryptographicOperations.FixedTimeEquals(key, other.key);

    public override bool Equals(object? obj) => obj is GroupInvitation other && Equals(other);

    public override int GetHashCode() => Record.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupInvitation? invitation)
    {
        invitation = null;
        if (body.Length <= GroupKey.Length
            || !GroupRecord.TryParse(body[GroupKey.Length..], out GroupRecord? record)
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

        invitation = new GroupInvitation(body[..GroupKey.Length].ToArray(), record);
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        key.CopyTo(body);
        Record.ToBytes().CopyTo(body[GroupKey.Length..]);
    }
}
