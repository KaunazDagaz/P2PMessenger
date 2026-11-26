using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Groups;

internal static class TestGroups
{
    public static async Task AddContactAsync(Messenger messenger, KeySet contact, string name, CancellationToken cancellationToken)
    {
        Assert.True((await messenger.Contacts.AddAsync(messenger.Contacts.Check(Code(contact, name)), cancellationToken)).IsSaved);
        Assert.True(await messenger.Contacts.ConfirmFirstConnectionAsync(contact.IdentityKey, contact.StaticKey, cancellationToken));
    }

    public static async Task<GroupRecord> InvitedByAsync(Messenger messenger, KeySet creator, string creatorName, string groupName, GroupKey key, CancellationToken cancellationToken)
    {
        GroupMember[] members = [new(messenger.Identity.Keys.IdentityKey, messenger.Identity.Name), new(creator.IdentityKey, Name(creatorName))];
        var record = GroupRecord.Create(GroupId.Create(), 1, Name(groupName), key, members, creator);
        Assert.True(await messenger.Groups.ReceiveInvitationAsync(creator.IdentityKey, GroupInvitation.Create(key, record), cancellationToken));
        Assert.True(messenger.Groups.TryGet(record.GroupId, out var group));
        Assert.Equal(Application.Groups.GroupState.Created, group.State);
        return record;
    }

    public static GroupMessage From(KeySet author, GroupRecord record, GroupKey key, long sequence, string text) =>
        GroupMessage.Seal(key, record.GroupId, record.Epoch, author, Text(sequence, text));

    public static GroupText Text(long sequence, string text) =>
        GroupText.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddMinutes(sequence), text, out var groupText) ? groupText : throw new InvalidOperationException();

    public static MessageId IdOf(GroupMessage message, GroupKey key) =>
        message.TryOpen(key, out var text) ? text.Id : throw new InvalidOperationException();
}
