using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

internal sealed class DatabaseGroupStore : IGroupStore
{
    private readonly GroupRepository groups;
    private readonly GroupMessageRepository messages;

    public DatabaseGroupStore(GroupRepository groups, GroupMessageRepository messages)
    {
        this.groups = groups;
        this.messages = messages;
    }

    public async ValueTask<IReadOnlyList<LoadedGroup>> LoadGroupsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredGroup> stored = await groups.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. stored.Select(static group => new LoadedGroup(
                group.Record,
                group.Key,
                [.. group.Members.Select(member => ToStatus(group.History, member))],
                group.AuthorityKey,
                group.History,
                group.LeaveProofs)),
        ];
    }

    public async ValueTask AddGroupAsync(GroupRecord record, GroupKey key, IReadOnlyList<GroupMemberStatus> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);

        await groups.AddAsync(new StoredGroup(record, key, [.. members.Select(ToStored)]), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UpdateMembersAsync(GroupId groupId, IReadOnlyCollection<GroupMemberStatus> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);

        await groups.UpdateMembersAsync(groupId, [.. members.Select(ToStored)], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AdvanceEpochAsync(
        GroupRecord record,
        GroupKey key,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> members,
        IReadOnlyCollection<Ed25519PublicKey> removeLeaveProofs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(members);

        await groups.AdvanceEpochAsync(
            record.GroupId,
            record,
            key,
            authorityKey,
            [.. members.Select(ToStored)],
            removeLeaveProofs,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveMembershipAsync(
        GroupId groupId,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> members,
        GroupLeave? saveLeaveProof,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);

        await groups.SaveMembershipAsync(
            groupId,
            authorityKey,
            [.. members.Select(ToStored)],
            saveLeaveProof,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<GroupChatMessage>> LoadMessagesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredGroupMessage> stored = await messages.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. stored.Select(static message => new GroupChatMessage(
            message.Envelope,
            message.Id,
            message.Sequence,
            message.WrittenAt,
            message.Text,
            message.RecordedAt,
            message.Deliveries.ToDictionary(static delivery => delivery.Key, static delivery => ToDelivery(delivery.Value))))];
    }

    public async ValueTask<bool> AddMessageAsync(GroupChatMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        StoredGroupMessage stored = new(message.GroupId, message.AuthorKey, message.Id, message.Sequence, message.WrittenAt, message.Text, message.RecordedAt, message.Envelope)
        {
            Deliveries = message.Deliveries.ToDictionary(static delivery => delivery.Key, static delivery => ToStored(delivery.Value)),
        };
        return await messages.AddAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> MarkReceivedAsync(GroupId groupId, Ed25519PublicKey memberKey, MessageId id, CancellationToken cancellationToken) =>
        await messages.MarkReceivedAsync(groupId, memberKey, id, cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>> LoadDeletedAsync(CancellationToken cancellationToken) =>
        await messages.LoadDeletedRangesAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<int> DeleteChatAsync(GroupId groupId, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken) =>
        await messages.DeleteConversationAsync(groupId, deleted, cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken) =>
        await messages.DeleteWaitingAsync(groupId, id, deleted, cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<GroupAcknowledgement>> LoadAcknowledgementsAsync(CancellationToken cancellationToken) =>
        await messages.LoadAcknowledgementsAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> AddAcknowledgementAsync(GroupAcknowledgement acknowledgement, CancellationToken cancellationToken) =>
        await messages.AddAcknowledgementAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> RemoveAcknowledgementAsync(GroupId groupId, Ed25519PublicKey recipientKey, MessageId messageId, CancellationToken cancellationToken) =>
        await messages.RemoveAcknowledgementAsync(groupId, recipientKey, messageId, cancellationToken).ConfigureAwait(false);

    private static GroupMemberStatus ToStatus(IReadOnlyList<GroupRecord> history, StoredGroupMember member)
    {
        DisplayName name = history
            .SelectMany(static record => record.Members)
            .First(recorded => recorded.IdentityKey.Equals(member.IdentityKey))
            .Name;
        return new GroupMemberStatus(
            member.IdentityKey,
            name,
            (GroupMemberState)(int)member.State,
            (MemberJoin)(int)member.Joined,
            (NoticeState)(int)member.Invitation,
            (NoticeState)(int)member.OwnJoin,
            (NoticeState)(int)member.Update,
            (NoticeState)(int)member.Leave,
            (NoticeState)(int)member.Removed,
            member.RemovedEpoch);
    }

    private static StoredGroupMember ToStored(GroupMemberStatus member) =>
        new(
            member.IdentityKey,
            (StoredGroupMemberState)(int)member.State,
            (StoredMemberJoin)(int)member.Joined,
            (StoredNoticeState)(int)member.Invitation,
            (StoredNoticeState)(int)member.OwnJoin,
            (StoredNoticeState)(int)member.Update,
            (StoredNoticeState)(int)member.Leave,
            (StoredNoticeState)(int)member.Removed,
            member.RemovedEpoch);

    private static DeliveryState ToDelivery(StoredDeliveryState stored) => stored switch
    {
        StoredDeliveryState.Waiting => DeliveryState.Waiting,
        StoredDeliveryState.Received => DeliveryState.Received,
        _ => DeliveryState.None,
    };

    private static StoredDeliveryState ToStored(DeliveryState delivery) => delivery switch
    {
        DeliveryState.Waiting => StoredDeliveryState.Waiting,
        DeliveryState.Received => StoredDeliveryState.Received,
        _ => StoredDeliveryState.None,
    };
}
