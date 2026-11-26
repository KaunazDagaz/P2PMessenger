using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class GroupRepository
{
    private const string SelectGroups = "SELECT id, shared_id_mac, shared_id_enc, name_enc FROM chat_group ORDER BY id;";

    private const string SelectKeys = "SELECT group_id, epoch, key_enc, record_enc FROM group_key ORDER BY group_id, epoch;";

    private const string SelectMembers =
        "SELECT group_member.group_id, group_member.person_id, person.identity_key_mac, person.identity_key_enc, group_member.state, "
        + "group_member.joined, group_member.invitation_state, group_member.join_state "
        + "FROM group_member JOIN person ON person.id = group_member.person_id "
        + "ORDER BY group_member.group_id, group_member.person_id;";

    private const string SelectGroupByLookup = "SELECT id FROM chat_group WHERE shared_id_mac = $shared_id_mac;";
    private const string SelectPersonByLookup = "SELECT id FROM person WHERE identity_key_mac = $identity_key_mac;";
    private const string NextGroupId = "SELECT coalesce(max(id), 0) + 1 FROM chat_group;";
    private const string NextPersonId = "SELECT coalesce(max(id), 0) + 1 FROM person;";

    private const string InsertGroup =
        "INSERT INTO chat_group (id, shared_id_mac, shared_id_enc, name_enc) VALUES ($id, $shared_id_mac, $shared_id_enc, $name_enc);";

    private const string InsertPerson =
        "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) "
        + "VALUES ($id, $identity_key_mac, $identity_key_enc, $display_name_enc);";

    private const string InsertMember =
        "INSERT INTO group_member (group_id, person_id, state, joined, invitation_state, join_state) "
        + "VALUES ($group_id, $person_id, 1, $joined, $invitation_state, $join_state);";

    private const string InsertKey =
        "INSERT INTO group_key (group_id, epoch, key_enc, record_enc) VALUES ($group_id, $epoch, $key_enc, $record_enc);";

    private const string InsertConversation = "INSERT INTO conversation (group_id) VALUES ($group_id);";

    private const string UpdateMember =
        "UPDATE group_member SET joined = $joined, invitation_state = $invitation_state, join_state = $join_state "
        + "WHERE group_id = $group_id AND state = 1 AND person_id = (SELECT id FROM person WHERE identity_key_mac = $identity_key_mac);";

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
        if (!group.Record.Fits(group.Key))
        {
            throw new ArgumentException("The key is not the one the record names.", nameof(group));
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
                int changed = 0;
                foreach (StoredGroupMember member in members)
                {
                    changed += await Sql.ExecuteAsync(
                        connection,
                        transaction,
                        UpdateMember,
                        token,
                        ("$group_id", row),
                        ("$identity_key_mac", keys.Lookup(LookupDomain.IdentityKey, member.IdentityKey.Bytes)),
                        ("$joined", (long)member.Joined),
                        ("$invitation_state", (long)member.Invitation),
                        ("$join_state", (long)member.OwnJoin)).ConfigureAwait(false);
                }

                return changed;
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
        if (!Enum.IsDefined(member.Joined) || !Enum.IsDefined(member.Invitation) || !Enum.IsDefined(member.OwnJoin))
        {
            throw new ArgumentException("A member has an unknown state.", parameterName);
        }
    }

    private static T Value<T>(long value, string what)
        where T : struct, Enum =>
        Enum.IsDefined(typeof(T), (int)value) && value is >= 0 and <= 2
            ? (T)Enum.ToObject(typeof(T), value)
            : throw StoredValues.Inconsistent(what);

    private async Task<IReadOnlyList<StoredGroup>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<long, (GroupId Id, DisplayName Name)> groups = [];
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

                groups.Add(row, (id, name));
            }
        }

        Dictionary<long, (GroupRecord Record, GroupKey Key)> current = [];
        try
        {
            using (SqliteCommand command = Sql.Command(connection, null, SelectKeys))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    long row = reader.GetInt64(0);
                    long epoch = reader.GetInt64(1);
                    if (!groups.TryGetValue(row, out (GroupId Id, DisplayName Name) group))
                    {
                        throw StoredValues.Inconsistent("a group key belongs to no group");
                    }

                    (GroupRecord Record, GroupKey Key) loaded = ReadKey(row, epoch, reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3), group.Id, group.Name);
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
                    if (reader.GetInt64(4) != 1)
                    {
                        throw StoredValues.Inconsistent("a group member has a state that this version does not know");
                    }

                    StoredGroupMember member = new(
                        identityKey,
                        Value<StoredMemberJoin>(reader.GetInt64(5), "a group member has an unknown join state"),
                        Value<StoredNoticeState>(reader.GetInt64(6), "a group member has an unknown invitation state"),
                        Value<StoredNoticeState>(reader.GetInt64(7), "a group member has an unknown join delivery state"));
                    if (!members.TryGetValue(row, out List<(long PersonId, StoredGroupMember Member)>? list))
                    {
                        list = [];
                        members.Add(row, list);
                    }

                    list.Add((personId, member));
                }
            }

            List<StoredGroup> loadedGroups = [];
            foreach ((long row, _) in groups)
            {
                if (!current.Remove(row, out (GroupRecord Record, GroupKey Key) state))
                {
                    throw StoredValues.Inconsistent("a group has no key");
                }

                List<StoredGroupMember> groupMembers = members.TryGetValue(row, out List<(long PersonId, StoredGroupMember Member)>? list)
                    ? [.. list.Select(static item => item.Member)]
                    : [];
                try
                {
                    CheckMembers(state.Record, groupMembers, "members");
                }
                catch (ArgumentException exception)
                {
                    state.Key.Dispose();
                    throw new StoredDataException(StoredValues.Inconsistent("a group's members do not match its record").Message, exception);
                }

                loadedGroups.Add(new StoredGroup(state.Record, state.Key, groupMembers));
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
            ("$name_enc", keys.Seal(Locations.GroupName(row), StoredValues.Utf8(record.Name.Value)))).ConfigureAwait(false);

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
                ("$joined", (long)state.Joined),
                ("$invitation_state", (long)state.Invitation),
                ("$join_state", (long)state.OwnJoin)).ConfigureAwait(false);
        }

        byte[] raw = new byte[GroupKey.Length];
        try
        {
            group.Key.Export(raw);
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertKey,
                cancellationToken,
                ("$group_id", row),
                ("$epoch", (long)record.Epoch),
                ("$key_enc", keys.Seal(Locations.GroupKey(row, record.Epoch), raw)),
                ("$record_enc", keys.Seal(Locations.GroupRecord(row, record.Epoch), record.ToBytes()))).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }

        await Sql.ExecuteAsync(connection, transaction, InsertConversation, cancellationToken, ("$group_id", row)).ConfigureAwait(false);
        return true;
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
