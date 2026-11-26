using System.Security.Cryptography;
using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

public sealed class InMemoryGroupStore : IGroupStore, IDisposable
{
    private readonly ISeenMessageIds seen;
    private readonly Lock gate = new();
    private readonly Dictionary<GroupId, (GroupRecord Record, byte[] Key, List<GroupMemberStatus> Members)> groups = [];
    private readonly List<GroupChatMessage> messages = [];
    private readonly Dictionary<GroupId, IReadOnlyList<DeletedRange>> deletedByGroup = [];

    public InMemoryGroupStore(ISeenMessageIds seen)
    {
        ArgumentNullException.ThrowIfNull(seen);

        this.seen = seen;
    }

    public ValueTask<IReadOnlyList<LoadedGroup>> LoadGroupsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            List<LoadedGroup> loaded = [];
            foreach ((GroupRecord record, byte[] key, List<GroupMemberStatus> members) in groups.Values)
            {
                loaded.Add(new LoadedGroup(record, GroupKey.TryImport(key, out GroupKey? copy) ? copy : throw new InvalidOperationException("A kept key cannot be read."), [.. members]));
            }

            return ValueTask.FromResult<IReadOnlyList<LoadedGroup>>(loaded);
        }
    }

    public ValueTask AddGroupAsync(GroupRecord record, GroupKey key, IReadOnlyList<GroupMemberStatus> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(members);

        byte[] raw = new byte[GroupKey.Length];
        key.Export(raw);
        lock (gate)
        {
            if (!groups.TryAdd(record.GroupId, (record, raw, [.. members])))
            {
                CryptographicOperations.ZeroMemory(raw);
                throw new InvalidOperationException("The group is already kept.");
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateMembersAsync(GroupId groupId, IReadOnlyCollection<GroupMemberStatus> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(members);

        lock (gate)
        {
            if (!groups.TryGetValue(groupId, out (GroupRecord Record, byte[] Key, List<GroupMemberStatus> Members) group))
            {
                throw new InvalidOperationException("The group is not kept.");
            }

            foreach (GroupMemberStatus member in members)
            {
                int index = group.Members.FindIndex(kept => kept.IdentityKey.Equals(member.IdentityKey));
                if (index >= 0)
                {
                    group.Members[index] = member;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<GroupChatMessage>> LoadMessagesAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<GroupChatMessage>>([.. messages]);
        }
    }

    public async ValueTask<bool> AddMessageAsync(GroupChatMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (gate)
        {
            if (messages.Exists(kept => kept.GroupId.Equals(message.GroupId) && kept.AuthorKey.Equals(message.AuthorKey) && kept.Sequence == message.Sequence))
            {
                return false;
            }

            messages.Add(message);
        }

        await seen.AddAsync(message.Id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask<bool> MarkReceivedAsync(GroupId groupId, Ed25519PublicKey memberKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(memberKey);
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            int index = messages.FindIndex(message => message.GroupId.Equals(groupId) && message.Id.Equals(id) && message.IsWaitingFor(memberKey));
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            messages[index] = messages[index].WithReceivedBy(memberKey);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<IReadOnlyDictionary<GroupId, IReadOnlyList<DeletedRange>>> LoadDeletedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<GroupId, IReadOnlyList<DeletedRange>>>(new Dictionary<GroupId, IReadOnlyList<DeletedRange>>(deletedByGroup));
        }
    }

    public ValueTask<int> DeleteChatAsync(GroupId groupId, IReadOnlyList<DeletedRange> deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(deleted);

        lock (gate)
        {
            int removed = messages.RemoveAll(message => message.GroupId.Equals(groupId));
            Keep(groupId, deleted);
            return ValueTask.FromResult(removed);
        }
    }

    public ValueTask<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<DeletedRange> deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(deleted);

        lock (gate)
        {
            int index = messages.FindIndex(message => message.GroupId.Equals(groupId)
                && message.Id.Equals(id)
                && message.Direction == MessageDirection.Outgoing
                && message.ReceivedCount == 0);
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            messages.RemoveAt(index);
            Keep(groupId, deleted);
            return ValueTask.FromResult(true);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach ((GroupRecord _, byte[] key, List<GroupMemberStatus> _) in groups.Values)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            groups.Clear();
        }
    }

    private void Keep(GroupId groupId, IReadOnlyList<DeletedRange> deleted)
    {
        if (deleted.Count > 0)
        {
            deletedByGroup[groupId] = [.. deleted];
        }
    }
}
