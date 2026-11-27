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
    private readonly Dictionary<GroupId, StoredEntry> groups = [];
    private readonly List<GroupChatMessage> messages = [];
    private readonly Dictionary<GroupId, IReadOnlyList<AuthorSequenceRange>> deletedByGroup = [];
    private readonly Dictionary<(GroupId GroupId, MessageId MessageId, Ed25519PublicKey RecipientKey), GroupAcknowledgement> acknowledgements = [];

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
            foreach (StoredEntry entry in groups.Values)
            {
                loaded.Add(new LoadedGroup(
                    entry.History[^1],
                    GroupKey.TryImport(entry.Key, out GroupKey? copy) ? copy : throw new InvalidOperationException("A kept key cannot be read."),
                    [.. entry.Members],
                    entry.AuthorityKey,
                    [.. entry.History],
                    [.. entry.LeaveProofs]));
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
            if (!groups.TryAdd(record.GroupId, new StoredEntry([record], raw, record.Signer, [.. members], [])))
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
            if (!groups.TryGetValue(groupId, out StoredEntry? group))
            {
                throw new InvalidOperationException("The group is not kept.");
            }

            UpdateMembersLocked(group.Members, members);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AdvanceEpochAsync(
        GroupRecord record,
        GroupKey key,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> members,
        IReadOnlyCollection<Ed25519PublicKey> removeLeaveProofs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(authorityKey);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(removeLeaveProofs);
        if (record.Epoch < 2 || !record.Fits(key) || !record.HasMember(authorityKey))
        {
            throw new ArgumentException("The epoch update record, key, or authority is not valid.", nameof(record));
        }

        byte[] raw = new byte[GroupKey.Length];
        key.Export(raw);
        lock (gate)
        {
            if (!groups.TryGetValue(record.GroupId, out StoredEntry? entry))
            {
                CryptographicOperations.ZeroMemory(raw);
                throw new InvalidOperationException("The group is not kept.");
            }

            CryptographicOperations.ZeroMemory(entry.Key);
            entry.Key = raw;
            entry.History.Add(record);
            entry.AuthorityKey = authorityKey;
            UpdateMembersLocked(entry.Members, members);
            if (removeLeaveProofs.Count > 0)
            {
                HashSet<Ed25519PublicKey> clear = [.. removeLeaveProofs];
                entry.LeaveProofs.RemoveAll(proof => clear.Contains(proof.MemberKey));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SaveMembershipAsync(
        GroupId groupId,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> members,
        GroupLeave? saveLeaveProof,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(authorityKey);
        ArgumentNullException.ThrowIfNull(members);

        lock (gate)
        {
            if (!groups.TryGetValue(groupId, out StoredEntry? entry))
            {
                throw new InvalidOperationException("The group is not kept.");
            }

            entry.AuthorityKey = authorityKey;
            UpdateMembersLocked(entry.Members, members);
            if (saveLeaveProof is not null)
            {
                entry.LeaveProofs.RemoveAll(proof => proof.MemberKey.Equals(saveLeaveProof.MemberKey));
                entry.LeaveProofs.Add(saveLeaveProof);
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

    public ValueTask<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>> LoadDeletedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>>(new Dictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>(deletedByGroup));
        }
    }

    public ValueTask<int> DeleteChatAsync(GroupId groupId, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(deleted);

        lock (gate)
        {
            int removed = messages.RemoveAll(message => message.GroupId.Equals(groupId));
            foreach ((GroupId GroupId, MessageId MessageId, Ed25519PublicKey RecipientKey) key in acknowledgements.Keys.Where(entry => entry.GroupId.Equals(groupId)).ToList())
            {
                acknowledgements.Remove(key);
            }

            Keep(groupId, deleted);
            return ValueTask.FromResult(removed);
        }
    }

    public ValueTask<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken)
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

    public ValueTask<IReadOnlyList<GroupAcknowledgement>> LoadAcknowledgementsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<GroupAcknowledgement>>([.. acknowledgements.Values]);
        }
    }

    public ValueTask<bool> AddAcknowledgementAsync(GroupAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (!acknowledgement.HasValidSignature())
        {
            throw new ArgumentException("The group acknowledgement does not carry a valid signature.", nameof(acknowledgement));
        }

        lock (gate)
        {
            GroupChatMessage? message = messages.Find(kept =>
                kept.GroupId.Equals(acknowledgement.GroupId)
                && kept.Id.Equals(acknowledgement.MessageId)
                && kept.Envelope.Epoch == acknowledgement.Epoch);
            if (message is null || message.Direction != MessageDirection.Incoming || message.AuthorKey.Equals(acknowledgement.RecipientKey))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(acknowledgements.TryAdd((acknowledgement.GroupId, acknowledgement.MessageId, acknowledgement.RecipientKey), acknowledgement));
        }
    }

    public ValueTask<bool> RemoveAcknowledgementAsync(GroupId groupId, Ed25519PublicKey recipientKey, MessageId messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(recipientKey);
        ArgumentNullException.ThrowIfNull(messageId);

        lock (gate)
        {
            return ValueTask.FromResult(acknowledgements.Remove((groupId, messageId, recipientKey)));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (StoredEntry entry in groups.Values)
            {
                CryptographicOperations.ZeroMemory(entry.Key);
            }

            groups.Clear();
        }
    }

    private static void UpdateMembersLocked(List<GroupMemberStatus> existing, IReadOnlyCollection<GroupMemberStatus> updates)
    {
        foreach (GroupMemberStatus member in updates)
        {
            int index = existing.FindIndex(kept => kept.IdentityKey.Equals(member.IdentityKey));
            if (index >= 0)
            {
                existing[index] = member;
            }
        }
    }

    private void Keep(GroupId groupId, IReadOnlyList<AuthorSequenceRange> deleted)
    {
        if (deleted.Count > 0)
        {
            deletedByGroup[groupId] = [.. deleted];
        }
    }

    private sealed class StoredEntry
    {
        public StoredEntry(
            List<GroupRecord> history,
            byte[] key,
            Ed25519PublicKey authorityKey,
            List<GroupMemberStatus> members,
            List<GroupLeave> leaveProofs)
        {
            History = history;
            Key = key;
            AuthorityKey = authorityKey;
            Members = members;
            LeaveProofs = leaveProofs;
        }

        public List<GroupRecord> History { get; }

        public byte[] Key { get; set; }

        public Ed25519PublicKey AuthorityKey { get; set; }

        public List<GroupMemberStatus> Members { get; }

        public List<GroupLeave> LeaveProofs { get; }
    }
}
