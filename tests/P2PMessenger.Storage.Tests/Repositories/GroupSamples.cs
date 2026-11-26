using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

internal static class GroupSamples
{
    public static (GroupRecord Record, GroupKey Key) Record(KeySet signer, string name, params (KeySet Keys, string Name)[] others)
    {
        var key = GroupKey.Create();
        List<GroupMember> members = [new(signer.IdentityKey, Samples.Name("Alice"))];
        members.AddRange(others.Select(other => new GroupMember(other.Keys.IdentityKey, Samples.Name(other.Name))));
        return (GroupRecord.Create(GroupId.Create(), 1, Samples.Name(name), key, members, signer), key);
    }

    public static StoredGroup Group(GroupRecord record, GroupKey key, Ed25519PublicKey own) =>
        new(
            record,
            key,
            [.. record.Members.Select(member => member.IdentityKey.Equals(own)
                ? new StoredGroupMember(member.IdentityKey, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None)
                : new StoredGroupMember(member.IdentityKey, StoredMemberJoin.NotYet, StoredNoticeState.Waiting, StoredNoticeState.None))]);

    public static StoredGroupMessage Message(GroupRecord record, GroupKey key, KeySet author, long sequence, string text, int minute, params Ed25519PublicKey[] waitingFor)
    {
        var groupText = GroupText.TryCreate(MessageId.Create(), sequence, Samples.WrittenAt.AddMinutes(minute), text, out var created) ? created : throw new InvalidOperationException();
        var envelope = GroupMessage.Seal(key, record.GroupId, record.Epoch, author, groupText);
        return new StoredGroupMessage(record.GroupId, author.IdentityKey, groupText.Id, sequence, groupText.WrittenAt, text, Samples.WrittenAt.AddMinutes(minute).AddTicks(3), envelope)
        {
            Deliveries = waitingFor.ToDictionary(recipient => recipient, _ => StoredDeliveryState.Waiting),
        };
    }
}
