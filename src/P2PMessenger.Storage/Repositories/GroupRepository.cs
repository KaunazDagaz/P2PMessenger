using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class GroupRepository
{
    private const string SelectOwnPerson = "SELECT person_id FROM identity WHERE id = 1;";

    private const string SelectGroups = "SELECT id, shared_id_mac, shared_id_enc, name_enc, authority_enc FROM chat_group ORDER BY id;";

    private const string SelectKeys = "SELECT group_id, epoch, key_enc, record_enc FROM group_key ORDER BY group_id, epoch;";

    private const string SelectMembers =
        "SELECT group_member.group_id, group_member.person_id, person.identity_key_mac, person.identity_key_enc, group_member.state, "
        + "group_member.joined, group_member.invitation_state, group_member.join_state, group_member.update_state, group_member.leave_state, "
        + "group_member.removed_state, group_member.removed_epoch "
        + "FROM group_member JOIN person ON person.id = group_member.person_id "
        + "ORDER BY group_member.group_id, group_member.person_id;";

    private const string SelectLeaveProofs =
        "SELECT proof.group_id, proof.member_person_id, person.identity_key_mac, person.identity_key_enc, proof.proof_enc "
        + "FROM group_leave_proof AS proof JOIN person ON person.id = proof.member_person_id "
        + "ORDER BY proof.group_id, proof.member_person_id;";

    private const string SelectGroupByLookup = "SELECT id FROM chat_group WHERE shared_id_mac = $shared_id_mac;";
    private const string SelectGroupNameByLookup = "SELECT id, name_enc FROM chat_group WHERE shared_id_mac = $shared_id_mac;";
    private const string SelectMaxEpoch = "SELECT max(epoch) FROM group_key WHERE group_id = $group_id;";
    private const string SelectPersonByLookup = "SELECT id FROM person WHERE identity_key_mac = $identity_key_mac;";
    private const string NextGroupId = "SELECT coalesce(max(id), 0) + 1 FROM chat_group;";
    private const string NextPersonId = "SELECT coalesce(max(id), 0) + 1 FROM person;";

    private const string InsertGroup =
        "INSERT INTO chat_group (id, shared_id_mac, shared_id_enc, name_enc, authority_enc) VALUES ($id, $shared_id_mac, $shared_id_enc, $name_enc, $authority_enc);";

    private const string InsertPerson =
        "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) "
        + "VALUES ($id, $identity_key_mac, $identity_key_enc, $display_name_enc);";

    private const string InsertMember =
        "INSERT INTO group_member (group_id, person_id, state, joined, invitation_state, join_state, update_state, leave_state, removed_state, removed_epoch) "
        + "VALUES ($group_id, $person_id, $state, $joined, $invitation_state, $join_state, $update_state, $leave_state, $removed_state, $removed_epoch);";

    private const string InsertKey =
        "INSERT INTO group_key (group_id, epoch, key_enc, record_enc) VALUES ($group_id, $epoch, $key_enc, $record_enc);";

    private const string InsertConversation = "INSERT INTO conversation (group_id) VALUES ($group_id);";

    private const string UpdateMember =
        "UPDATE group_member SET state = $state, joined = $joined, invitation_state = $invitation_state, join_state = $join_state, "
        + "update_state = $update_state, leave_state = $leave_state, removed_state = $removed_state, removed_epoch = $removed_epoch "
        + "WHERE group_id = $group_id AND person_id = (SELECT id FROM person WHERE identity_key_mac = $identity_key_mac);";

    private const string UpdateAuthority = "UPDATE chat_group SET authority_enc = $authority_enc WHERE id = $group_id;";

    private const string UpsertLeaveProof =
        "INSERT INTO group_leave_proof (group_id, member_person_id, proof_enc) VALUES ($group_id, $member_person_id, $proof_enc) "
        + "ON CONFLICT (group_id, member_person_id) DO UPDATE SET proof_enc = excluded.proof_enc;";

    private const string DeleteLeaveProof =
        "DELETE FROM group_leave_proof WHERE group_id = $group_id AND member_person_id = (SELECT id FROM person WHERE identity_key_mac = $identity_key_mac);";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal GroupRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<IReadOnlyList<StoredGroup>> LoadAllAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAllAsync, cancellationToken);

    public Task AddAsync(StoredGroup group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(group.Record);
        ArgumentNullException.ThrowIfNull(group.Key);
        ArgumentNullException.ThrowIfNull(group.Members);
        ArgumentNullException.ThrowIfNull(group.AuthorityKey);
        if (!group.Record.Fits(group.Key))
        {
            throw new ArgumentException("The key is not the one the record names.", nameof(group));
        }

        if (!group.Record.HasMember(group.AuthorityKey))
        {
            throw new ArgumentException("The group authority must be one of the record's members.", nameof(group));
        }

        CheckMembers(group.Record, group.Members, nameof(group));
        return gate.WriteAsync((connection, transaction, token) => AddAsync(connection, transaction, group, token), cancellationToken);
    }

    public Task<int> UpdateMembersAsync(GroupId groupId, IReadOnlyCollection<StoredGroupMember> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(members);
        foreach (StoredGroupMember member in members)
        {
            CheckValues(member, nameof(members));
        }

        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                long row = await Sql.LongAsync(connection, transaction, SelectGroupByLookup, token, ("$shared_id_mac", groupLookup)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The group is not stored.");
                return await UpdateMemberRowsAsync(connection, transaction, row, members, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task AdvanceEpochAsync(
        GroupId groupId,
        GroupRecord record,
        GroupKey key,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<StoredGroupMember> members,
        IReadOnlyCollection<Ed25519PublicKey> removeLeaveProofs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(authorityKey);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(removeLeaveProofs);
        if (!record.GroupId.Equals(groupId) || record.Epoch < 2 || !record.Fits(key) || !record.HasMember(authorityKey))
        {
            throw new ArgumentException("The epoch update record, key, or authority does not match the group.", nameof(record));
        }

        foreach (StoredGroupMember member in members)
        {
            CheckValues(member, nameof(members));
        }

        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                (long row, DisplayName storedName) = await ReadGroupHeaderAsync(connection, transaction, groupLookup, token).ConfigureAwait(false);
                if (!record.Name.Equals(storedName))
                {
                    throw new InvalidOperationException("The group name in the update record does not match the stored group.");
                }

                long maxEpoch = await Sql.LongAsync(connection, transaction, SelectMaxEpoch, token, ("$group_id", row)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The group has no stored epoch.");
                if (record.Epoch <= maxEpoch)
                {
                    throw new InvalidOperationException("The new epoch must be higher than the stored epoch.");
                }

                await InsertKeyAndRecordAsync(connection, transaction, row, record, key, token).ConfigureAwait(false);
                await Sql.ExecuteAsync(
                    connection,
                    transaction,
                    UpdateAuthority,
                    token,
                    ("$group_id", row),
                    ("$authority_enc", keys.Seal(Locations.GroupAuthority(row), authorityKey.Bytes))).ConfigureAwait(false);
                await UpdateMemberRowsAsync(connection, transaction, row, members, token).ConfigureAwait(false);

                foreach (Ed25519PublicKey keyToDelete in removeLeaveProofs)
                {
                    await Sql.ExecuteAsync(
                        connection,
                        transaction,
                        DeleteLeaveProof,
                        token,
                        ("$group_id", row),
                        ("$identity_key_mac", keys.Lookup(LookupDomain.IdentityKey, keyToDelete.Bytes))).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken);
    }

    public Task SaveMembershipAsync(
        GroupId groupId,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<StoredGroupMember> members,
        GroupLeave? saveLeaveProof,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(authorityKey);
        ArgumentNullException.ThrowIfNull(members);
        foreach (StoredGroupMember member in members)
        {
            CheckValues(member, nameof(members));
        }

        if (saveLeaveProof is not null && (!saveLeaveProof.GroupId.Equals(groupId) || !saveLeaveProof.HasValidSignature()))
        {
            throw new ArgumentException("The leave notice does not match the group or lacks a valid signature.", nameof(saveLeaveProof));
        }

        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                long row = await Sql.LongAsync(connection, transaction, SelectGroupByLookup, token, ("$shared_id_mac", groupLookup)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The group is not stored.");
                await Sql.ExecuteAsync(
                    connection,
                    transaction,
                    UpdateAuthority,
                    token,
                    ("$group_id", row),
                    ("$authority_enc", keys.Seal(Locations.GroupAuthority(row), authorityKey.Bytes))).ConfigureAwait(false);
                await UpdateMemberRowsAsync(connection, transaction, row, members, token).ConfigureAwait(false);

                if (saveLeaveProof is not null)
                {
                    long personId = await Sql.LongAsync(
                        connection,
                        transaction,
                        SelectPersonByLookup,
                        token,
                        ("$identity_key_mac", keys.Lookup(LookupDomain.IdentityKey, saveLeaveProof.MemberKey.Bytes))).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The leaving member is not stored.");
                    await Sql.ExecuteAsync(
                        connection,
                        transaction,
                        UpsertLeaveProof,
                        token,
                        ("$group_id", row),
                        ("$member_person_id", personId),
                        ("$proof_enc", keys.Seal(Locations.GroupLeaveProof(row, personId), saveLeaveProof.ToBytes()))).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken);
    }

    private static void CheckMembers(GroupRecord record, IReadOnlyList<StoredGroupMember> members, string parameterName)
    {
        if (members.Count != record.Members.Count)
        {
            throw new ArgumentException("Every member of the record needs exactly one state.", parameterName);
        }

        foreach (StoredGroupMember member in members)
        {
            CheckValues(member, parameterName);
            if (!record.HasMember(member.IdentityKey))
            {
                throw new ArgumentException("A state names a person who is not a member of the record.", parameterName);
            }
        }

        if (members.Select(static member => Convert.ToHexString(member.IdentityKey.Bytes)).Distinct(StringComparer.Ordinal).Count() != members.Count)
        {
            throw new ArgumentException("A member has two states.", parameterName);
        }
    }

    private static void CheckValues(StoredGroupMember member, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(member.IdentityKey);
        if (!Enum.IsDefined(member.State)
            || !Enum.IsDefined(member.Joined)
            || !Enum.IsDefined(member.Invitation)
            || !Enum.IsDefined(member.OwnJoin)
            || !Enum.IsDefined(member.Update)
            || !Enum.IsDefined(member.Leave)
            || !Enum.IsDefined(member.Removed)
            || member.RemovedEpoch < 0)
        {
            throw new ArgumentException("A member has an unknown state.", parameterName);
        }

        if (member.State != StoredGroupMemberState.Removed && (member.Removed != StoredNoticeState.None || member.RemovedEpoch != 0))
        {
            throw new ArgumentException("Only a removed member can carry removal state.", parameterName);
        }

        if (member.State == StoredGroupMemberState.Removed && member.RemovedEpoch != 0 && member.RemovedEpoch < 2)
        {
            throw new ArgumentException("A removal epoch must be at least 2.", parameterName);
        }

        if (member.Removed != StoredNoticeState.None && member.RemovedEpoch < 2)
        {
            throw new ArgumentException("A pending or delivered removal notice needs a removal epoch of at least 2.", parameterName);
        }
    }

    private static T Value<T>(long value, int min, int max, string what)
        where T : struct, Enum =>
        Enum.IsDefined(typeof(T), (int)value) && value >= min && value <= max
            ? (T)Enum.ToObject(typeof(T), value)
            : throw StoredValues.Inconsistent(what);

    private static bool HasNoNotices(StoredGroupMember member) =>
        member.Invitation == StoredNoticeState.None
        && member.OwnJoin == StoredNoticeState.None
        && member.Update == StoredNoticeState.None
        && member.Leave == StoredNoticeState.None
        && member.Removed == StoredNoticeState.None;

    private static void ValidateLoadedMembers(
        IReadOnlyList<GroupRecord> history,
        Ed25519PublicKey authority,
        IReadOnlyList<(long PersonId, StoredGroupMember Member)> members,
        IReadOnlyList<GroupLeave> leaveProofs,
        long? ownPersonId)
    {
        if (!history.Any(record => record.HasMember(authority)))
        {
            throw StoredValues.Inconsistent("the group authority is not in the group's history");
        }

        GroupRecord latest = history[^1];
        HashSet<Ed25519PublicKey> seenKeys = [];
        Dictionary<Ed25519PublicKey, (long PersonId, StoredGroupMember Member)> byKey = [];
        foreach ((long personId, StoredGroupMember member) in members)
        {
            if (!seenKeys.Add(member.IdentityKey))
            {
                throw StoredValues.Inconsistent("a group member appears more than once");
            }

            if (!history.Any(record => record.HasMember(member.IdentityKey)))
            {
                throw StoredValues.Inconsistent("a stored group member does not appear in any group record");
            }

            if (member.State != StoredGroupMemberState.Removed && (member.Removed != StoredNoticeState.None || member.RemovedEpoch != 0))
            {
                throw StoredValues.Inconsistent("a non-removed member carries removal state");
            }

            if (member.State == StoredGroupMemberState.Removed
                && ((member.RemovedEpoch != 0 && member.RemovedEpoch < 2) || (member.Removed != StoredNoticeState.None && member.RemovedEpoch < 2)))
            {
                throw StoredValues.Inconsistent("a removed member has an invalid removal epoch");
            }

            byKey.Add(member.IdentityKey, (personId, member));
        }

        foreach (GroupMember recordMember in latest.Members)
        {
            if (!byKey.ContainsKey(recordMember.IdentityKey))
            {
                throw StoredValues.Inconsistent("a member of the latest group record is missing");
            }
        }

        foreach (GroupLeave proof in leaveProofs)
        {
            if (!history.Any(record => record.HasMember(proof.MemberKey)))
            {
                throw StoredValues.Inconsistent("a leave proof belongs to no member of the group");
            }

            if (proof.HasSuccessor && !history.Any(record => record.HasMember(proof.SuccessorKey!)))
            {
                throw StoredValues.Inconsistent("a handover leave proof names someone outside the group");
            }
        }

        StoredGroupMember? ownMember = ownPersonId is { } ownId
            ? members.FirstOrDefault(item => item.PersonId == ownId).Member
            : null;
        if (ownPersonId is not null && ownMember is null)
        {
            throw StoredValues.Inconsistent("a stored group has no row for the user");
        }

        if (ownMember?.State == StoredGroupMemberState.Removed)
        {
            if (members.Any(item => !HasNoNotices(item.Member)))
            {
                throw StoredValues.Inconsistent("a removed user's group holds outgoing notice states");
            }

            return;
        }

        if (ownMember?.State == StoredGroupMemberState.Left)
        {
            if (!HasNoNotices(ownMember))
            {
                throw StoredValues.Inconsistent("the user who left carries outgoing notice states");
            }

            foreach ((long _, StoredGroupMember member) in members)
            {
                if (member.State == StoredGroupMemberState.Member)
                {
                    if (!latest.HasMember(member.IdentityKey)
                        || member.Invitation != StoredNoticeState.None
                        || member.OwnJoin != StoredNoticeState.None
                        || member.Update != StoredNoticeState.None
                        || member.Removed != StoredNoticeState.None)
                    {
                        throw StoredValues.Inconsistent("a left group carries invalid active member notice state");
                    }
                }
                else if (!HasNoNotices(member))
                {
                    throw StoredValues.Inconsistent("a former member of a left group carries notice state");
                }
            }

            return;
        }

        List<StoredGroupMember> active = [.. members.Select(static item => item.Member).Where(static member => member.State == StoredGroupMemberState.Member)];
        foreach (StoredGroupMember member in active)
        {
            if (!latest.HasMember(member.IdentityKey) || member.Leave != StoredNoticeState.None || member.Removed != StoredNoticeState.None)
            {
                throw StoredValues.Inconsistent("an active group member does not match the latest record or carries leave state");
            }

            if (active.Count < GroupRecord.MinMembers
                && (member.Invitation != StoredNoticeState.None || member.OwnJoin != StoredNoticeState.None || member.Update != StoredNoticeState.None))
            {
                throw StoredValues.Inconsistent("an ended group carries outgoing notice state");
            }
        }

        foreach ((long _, StoredGroupMember member) in members)
        {
            if (member.State == StoredGroupMemberState.Member)
            {
                continue;
            }

            if (member.State == StoredGroupMemberState.Left)
            {
                if (!HasNoNotices(member))
                {
                    throw StoredValues.Inconsistent("a left member carries outgoing notice states");
                }
            }
            else if (member.Invitation != StoredNoticeState.None
                || member.OwnJoin != StoredNoticeState.None
                || member.Update != StoredNoticeState.None
                || member.Leave != StoredNoticeState.None)
            {
                throw StoredValues.Inconsistent("a removed member carries non-removal notice state");
            }

            if (active.Count >= GroupRecord.MinMembers && latest.HasMember(member.IdentityKey))
            {
                bool hasProof = member.State == StoredGroupMemberState.Left && leaveProofs.Any(proof => proof.MemberKey.Equals(member.IdentityKey));
                if (!hasProof)
                {
                    throw StoredValues.Inconsistent("a member of the latest group record is inactive without a leave proof");
                }
            }
        }
    }

    private async Task<IReadOnlyList<StoredGroup>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long? ownPersonId = await Sql.LongAsync(connection, null, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        Dictionary<long, (GroupId Id, DisplayName Name, Ed25519PublicKey? Authority)> groups = [];
        using (SqliteCommand command = Sql.Command(connection, null, SelectGroups))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long row = reader.GetInt64(0);
                RecordLocation idLocation = Locations.GroupSharedId(row);
                byte[] idBytes = StoredValues.Open(keys, idLocation, reader.GetFieldValue<byte[]>(2));
                if (!GroupId.TryCreate(idBytes, out GroupId? id) || !keys.MatchesLookup(LookupDomain.GroupId, idBytes, reader.GetFieldValue<byte[]>(1)))
                {
                    throw StoredValues.Damaged(idLocation);
                }

                RecordLocation nameLocation = Locations.GroupName(row);
                string text = StoredValues.Text(nameLocation, StoredValues.Open(keys, nameLocation, reader.GetFieldValue<byte[]>(3)));
                if (!DisplayName.TryCreate(text, out DisplayName? name) || !string.Equals(name.Value, text, StringComparison.Ordinal))
                {
                    throw StoredValues.Damaged(nameLocation);
                }

                Ed25519PublicKey? authority = null;
                if (!reader.IsDBNull(4))
                {
                    RecordLocation authorityLocation = Locations.GroupAuthority(row);
                    byte[] authorityBytes = StoredValues.Open(keys, authorityLocation, reader.GetFieldValue<byte[]>(4));
                    if (!Ed25519PublicKey.TryCreate(authorityBytes, out authority))
                    {
                        throw StoredValues.Damaged(authorityLocation);
                    }
                }

                groups.Add(row, (id, name, authority));
            }
        }

        Dictionary<long, (GroupRecord Record, GroupKey Key)> current = [];
        Dictionary<long, List<GroupRecord>> histories = [];
        try
        {
            using (SqliteCommand command = Sql.Command(connection, null, SelectKeys))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    long row = reader.GetInt64(0);
                    long epoch = reader.GetInt64(1);
                    if (!groups.TryGetValue(row, out (GroupId Id, DisplayName Name, Ed25519PublicKey? Authority) group))
                    {
                        throw StoredValues.Inconsistent("a group key belongs to no group");
                    }

                    (GroupRecord Record, GroupKey Key) loaded = ReadKey(row, epoch, reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3), group.Id, group.Name);
                    if (!histories.TryGetValue(row, out List<GroupRecord>? history))
                    {
                        history = [];
                        histories.Add(row, history);
                    }

                    if (history.Count > 0 && loaded.Record.Epoch <= history[^1].Epoch)
                    {
                        loaded.Key.Dispose();
                        throw StoredValues.Inconsistent("group epochs are not strictly increasing");
                    }

                    history.Add(loaded.Record);
                    if (current.Remove(row, out (GroupRecord Record, GroupKey Key) older))
                    {
                        older.Key.Dispose();
                    }

                    current.Add(row, loaded);
                }
            }

            Dictionary<long, List<(long PersonId, StoredGroupMember Member)>> members = [];
            using (SqliteCommand command = Sql.Command(connection, null, SelectMembers))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    long row = reader.GetInt64(0);
                    long personId = reader.GetInt64(1);
                    Ed25519PublicKey identityKey = StoredValues.IdentityKey(keys, personId, reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
                    StoredGroupMemberState state = Value<StoredGroupMemberState>(reader.GetInt64(4), 1, 3, "a group member has an unknown membership state");
                    long removedEpochValue = reader.GetInt64(11);
                    if (removedEpochValue is < 0 or > GroupRecord.MaxEpoch)
                    {
                        throw StoredValues.Inconsistent("a group member has an invalid removal epoch");
                    }

                    StoredGroupMember member = new(
                        identityKey,
                        state,
                        Value<StoredMemberJoin>(reader.GetInt64(5), 0, 2, "a group member has an unknown join state"),
                        Value<StoredNoticeState>(reader.GetInt64(6), 0, 2, "a group member has an unknown invitation state"),
                        Value<StoredNoticeState>(reader.GetInt64(7), 0, 2, "a group member has an unknown join delivery state"),
                        Value<StoredNoticeState>(reader.GetInt64(8), 0, 2, "a group member has an unknown update delivery state"),
                        Value<StoredNoticeState>(reader.GetInt64(9), 0, 2, "a group member has an unknown leave delivery state"),
                        Value<StoredNoticeState>(reader.GetInt64(10), 0, 2, "a group member has an unknown removal delivery state"),
                        (int)removedEpochValue);
                    if (!members.TryGetValue(row, out List<(long PersonId, StoredGroupMember Member)>? list))
                    {
                        list = [];
                        members.Add(row, list);
                    }

                    list.Add((personId, member));
                }
            }

            Dictionary<long, List<GroupLeave>> leaveProofs = [];
            using (SqliteCommand command = Sql.Command(connection, null, SelectLeaveProofs))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    long row = reader.GetInt64(0);
                    long personId = reader.GetInt64(1);
                    if (!groups.TryGetValue(row, out (GroupId Id, DisplayName Name, Ed25519PublicKey? Authority) group))
                    {
                        throw StoredValues.Inconsistent("a leave proof belongs to no group");
                    }

                    Ed25519PublicKey memberKey = StoredValues.IdentityKey(keys, personId, reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
                    RecordLocation proofLocation = Locations.GroupLeaveProof(row, personId);
                    byte[] proofBytes = StoredValues.Open(keys, proofLocation, reader.GetFieldValue<byte[]>(4));
                    if (!GroupLeave.TryParse(proofBytes, out GroupLeave? proof)
                        || !proof.GroupId.Equals(group.Id)
                        || !proof.MemberKey.Equals(memberKey)
                        || !proof.HasValidSignature())
                    {
                        throw StoredValues.Damaged(proofLocation);
                    }

                    if (!leaveProofs.TryGetValue(row, out List<GroupLeave>? list))
                    {
                        list = [];
                        leaveProofs.Add(row, list);
                    }

                    list.Add(proof);
                }
            }

            List<StoredGroup> loadedGroups = [];
            foreach ((long row, (GroupId _, DisplayName _, Ed25519PublicKey? storedAuthority)) in groups)
            {
                if (!current.Remove(row, out (GroupRecord Record, GroupKey Key) state) || !histories.TryGetValue(row, out List<GroupRecord>? history))
                {
                    throw StoredValues.Inconsistent("a group has no key");
                }

                Ed25519PublicKey authority = storedAuthority ?? state.Record.Signer;
                List<(long PersonId, StoredGroupMember Member)> groupMembers = members.TryGetValue(row, out List<(long PersonId, StoredGroupMember Member)>? list)
                    ? list
                    : [];
                List<GroupLeave> groupProofs = leaveProofs.TryGetValue(row, out List<GroupLeave>? proofs)
                    ? proofs
                    : [];

                try
                {
                    ValidateLoadedMembers(history, authority, groupMembers, groupProofs, ownPersonId);
                }
                catch
                {
                    state.Key.Dispose();
                    throw;
                }

                loadedGroups.Add(new StoredGroup(
                    state.Record,
                    state.Key,
                    [.. groupMembers.Select(static item => item.Member)],
                    authority,
                    history.AsReadOnly(),
                    groupProofs.AsReadOnly()));
            }

            return loadedGroups;
        }
        catch
        {
            foreach ((GroupRecord _, GroupKey key) in current.Values)
            {
                key.Dispose();
            }

            throw;
        }
    }

    private (GroupRecord Record, GroupKey Key) ReadKey(long row, long epoch, byte[] sealedKey, byte[] sealedRecord, GroupId groupId, DisplayName name)
    {
        RecordLocation recordLocation = Locations.GroupRecord(row, epoch);
        byte[] recordBytes = StoredValues.Open(keys, recordLocation, sealedRecord);
        if (!GroupRecord.TryParse(recordBytes, out GroupRecord? record)
            || record.Epoch != epoch
            || !record.GroupId.Equals(groupId)
            || !record.Name.Equals(name))
        {
            throw StoredValues.Damaged(recordLocation);
        }

        RecordLocation keyLocation = Locations.GroupKey(row, epoch);
        byte[] raw = StoredValues.Open(keys, keyLocation, sealedKey);
        try
        {
            if (!GroupKey.TryImport(raw, out GroupKey? key))
            {
                throw StoredValues.Damaged(keyLocation);
            }

            if (!record.Fits(key))
            {
                key.Dispose();
                throw StoredValues.Damaged(keyLocation);
            }

            return (record, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    private async Task<(long GroupRow, DisplayName Name)> ReadGroupHeaderAsync(SqliteConnection connection, SqliteTransaction transaction, byte[] groupLookup, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Sql.Command(connection, transaction, SelectGroupNameByLookup, ("$shared_id_mac", groupLookup));
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The group is not stored.");
        }

        long row = reader.GetInt64(0);
        RecordLocation nameLocation = Locations.GroupName(row);
        string text = StoredValues.Text(nameLocation, StoredValues.Open(keys, nameLocation, reader.GetFieldValue<byte[]>(1)));
        if (!DisplayName.TryCreate(text, out DisplayName? name) || !string.Equals(name.Value, text, StringComparison.Ordinal))
        {
            throw StoredValues.Damaged(nameLocation);
        }

        return (row, name);
    }

    private async Task<int> UpdateMemberRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long groupRow,
        IReadOnlyCollection<StoredGroupMember> members,
        CancellationToken cancellationToken)
    {
        int changed = 0;
        foreach (StoredGroupMember member in members)
        {
            changed += await Sql.ExecuteAsync(
                connection,
                transaction,
                UpdateMember,
                cancellationToken,
                ("$group_id", groupRow),
                ("$identity_key_mac", keys.Lookup(LookupDomain.IdentityKey, member.IdentityKey.Bytes)),
                ("$state", (long)member.State),
                ("$joined", (long)member.Joined),
                ("$invitation_state", (long)member.Invitation),
                ("$join_state", (long)member.OwnJoin),
                ("$update_state", (long)member.Update),
                ("$leave_state", (long)member.Leave),
                ("$removed_state", (long)member.Removed),
                ("$removed_epoch", (long)member.RemovedEpoch)).ConfigureAwait(false);
        }

        return changed;
    }

    private async Task<bool> AddAsync(SqliteConnection connection, SqliteTransaction transaction, StoredGroup group, CancellationToken cancellationToken)
    {
        GroupRecord record = group.Record;
        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, record.GroupId.Bytes);
        long row = await Sql.LongAsync(connection, transaction, NextGroupId, cancellationToken).ConfigureAwait(false) ?? 1;
        await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertGroup,
            cancellationToken,
            ("$id", row),
            ("$shared_id_mac", groupLookup),
            ("$shared_id_enc", keys.Seal(Locations.GroupSharedId(row), record.GroupId.Bytes)),
            ("$name_enc", keys.Seal(Locations.GroupName(row), StoredValues.Utf8(record.Name.Value))),
            ("$authority_enc", keys.Seal(Locations.GroupAuthority(row), group.AuthorityKey.Bytes))).ConfigureAwait(false);

        foreach (GroupMember member in record.Members)
        {
            long personId = await PersonAsync(connection, transaction, member, cancellationToken).ConfigureAwait(false);
            StoredGroupMember state = group.Members.First(stored => stored.IdentityKey.Equals(member.IdentityKey));
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertMember,
                cancellationToken,
                ("$group_id", row),
                ("$person_id", personId),
                ("$state", (long)state.State),
                ("$joined", (long)state.Joined),
                ("$invitation_state", (long)state.Invitation),
                ("$join_state", (long)state.OwnJoin),
                ("$update_state", (long)state.Update),
                ("$leave_state", (long)state.Leave),
                ("$removed_state", (long)state.Removed),
                ("$removed_epoch", (long)state.RemovedEpoch)).ConfigureAwait(false);
        }

        await InsertKeyAndRecordAsync(connection, transaction, row, record, group.Key, cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync(connection, transaction, InsertConversation, cancellationToken, ("$group_id", row)).ConfigureAwait(false);
        return true;
    }

    private async Task InsertKeyAndRecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long groupRow,
        GroupRecord record,
        GroupKey key,
        CancellationToken cancellationToken)
    {
        byte[] raw = new byte[GroupKey.Length];
        try
        {
            key.Export(raw);
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertKey,
                cancellationToken,
                ("$group_id", groupRow),
                ("$epoch", (long)record.Epoch),
                ("$key_enc", keys.Seal(Locations.GroupKey(groupRow, record.Epoch), raw)),
                ("$record_enc", keys.Seal(Locations.GroupRecord(groupRow, record.Epoch), record.ToBytes()))).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    private async Task<long> PersonAsync(SqliteConnection connection, SqliteTransaction transaction, GroupMember member, CancellationToken cancellationToken)
    {
        byte[] lookup = keys.Lookup(LookupDomain.IdentityKey, member.IdentityKey.Bytes);
        if (await Sql.LongAsync(connection, transaction, SelectPersonByLookup, cancellationToken, ("$identity_key_mac", lookup)).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        long personId = await Sql.LongAsync(connection, transaction, NextPersonId, cancellationToken).ConfigureAwait(false) ?? 1;
        await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertPerson,
            cancellationToken,
            ("$id", personId),
            ("$identity_key_mac", lookup),
            ("$identity_key_enc", keys.Seal(Locations.PersonIdentityKey(personId), member.IdentityKey.Bytes)),
            ("$display_name_enc", keys.Seal(Locations.PersonName(personId), StoredValues.Utf8(member.Name.Value)))).ConfigureAwait(false);
        return personId;
    }
}
