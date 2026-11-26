using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

public interface IGroupStore
{
    ValueTask<IReadOnlyList<LoadedGroup>> LoadGroupsAsync(CancellationToken cancellationToken);

    ValueTask AddGroupAsync(GroupRecord record, GroupKey key, IReadOnlyList<GroupMemberStatus> members, CancellationToken cancellationToken);

    ValueTask UpdateMembersAsync(GroupId groupId, IReadOnlyCollection<GroupMemberStatus> members, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<GroupChatMessage>> LoadMessagesAsync(CancellationToken cancellationToken);

    ValueTask<bool> AddMessageAsync(GroupChatMessage message, CancellationToken cancellationToken);

    ValueTask<bool> MarkReceivedAsync(GroupId groupId, Ed25519PublicKey memberKey, MessageId id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<GroupId, IReadOnlyList<DeletedRange>>> LoadDeletedAsync(CancellationToken cancellationToken);

    ValueTask<int> DeleteChatAsync(GroupId groupId, IReadOnlyList<DeletedRange> deleted, CancellationToken cancellationToken);

    ValueTask<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<DeletedRange> deleted, CancellationToken cancellationToken);
}
