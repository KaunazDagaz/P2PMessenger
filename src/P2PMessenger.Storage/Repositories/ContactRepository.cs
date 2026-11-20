using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class ContactRepository
{
    private const string SelectContacts =
        "SELECT person.id, person.identity_key_mac, person.identity_key_enc, person.display_name_enc, contact.code_version, contact.static_key_mac, "
        + "contact.static_key_enc, contact.onion_key_enc, contact.code_signature_enc, contact.added_via, contact.status, contact.key_checked "
        + "FROM contact JOIN person ON person.id = contact.person_id ORDER BY person.id;";

    private const string SelectPersonByLookup = "SELECT id FROM person WHERE identity_key_mac = $identity_key_mac;";
    private const string SelectOwnPerson = "SELECT person_id FROM identity WHERE id = 1;";
    private const string NextPersonId = "SELECT coalesce(max(id), 0) + 1 FROM person;";

    private const string InsertPerson =
        "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) "
        + "VALUES ($id, $identity_key_mac, $identity_key_enc, $display_name_enc);";

    private const string UpdatePersonName = "UPDATE person SET display_name_enc = $display_name_enc WHERE id = $id;";

    private const string UpsertContact =
        "INSERT INTO contact (person_id, code_version, static_key_mac, static_key_enc, onion_key_enc, code_signature_enc, added_via, status, key_checked) "
        + "VALUES ($person_id, $code_version, $static_key_mac, $static_key_enc, $onion_key_enc, $code_signature_enc, $added_via, $status, $key_checked) "
        + "ON CONFLICT (person_id) DO UPDATE SET code_version = excluded.code_version, static_key_mac = excluded.static_key_mac, "
        + "static_key_enc = excluded.static_key_enc, onion_key_enc = excluded.onion_key_enc, code_signature_enc = excluded.code_signature_enc, "
        + "added_via = excluded.added_via, status = excluded.status, key_checked = excluded.key_checked;";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal ContactRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<IReadOnlyList<StoredContact>> LoadAllAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAllAsync, cancellationToken);

    public Task SaveAsync(StoredContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(contact.Code);
        if (!Enum.IsDefined(contact.AddedVia) || !Enum.IsDefined(contact.Status))
        {
            throw new ArgumentException("The contact has an unknown channel or status.", nameof(contact));
        }

        return gate.WriteAsync((connection, transaction, token) => SaveAsync(connection, transaction, contact, token), cancellationToken);
    }

    private async Task<IReadOnlyList<StoredContact>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        List<StoredContact> contacts = [];
        using SqliteCommand command = Sql.Command(connection, null, SelectContacts);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            contacts.Add(Read(reader));
        }

        return contacts;
    }

    private StoredContact Read(SqliteDataReader reader)
    {
        long personId = reader.GetInt64(0);
        Ed25519PublicKey identityKey = StoredValues.IdentityKey(keys, personId, reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2));
        DisplayName name = StoredValues.Name(keys, personId, reader.GetFieldValue<byte[]>(3));
        long version = reader.GetInt64(4);

        RecordLocation staticLocation = Locations.ContactStaticKey(personId);
        byte[] staticBytes = StoredValues.Open(keys, staticLocation, reader.GetFieldValue<byte[]>(6));
        if (!X25519PublicKey.TryCreate(staticBytes, out X25519PublicKey? staticKey)
            || !keys.MatchesLookup(LookupDomain.StaticKey, staticBytes, reader.GetFieldValue<byte[]>(5)))
        {
            throw StoredValues.Damaged(staticLocation);
        }

        RecordLocation onionLocation = Locations.ContactOnionKey(personId);
        if (!Ed25519PublicKey.TryCreate(StoredValues.Open(keys, onionLocation, reader.GetFieldValue<byte[]>(7)), out Ed25519PublicKey? onionKey))
        {
            throw StoredValues.Damaged(onionLocation);
        }

        byte[] signature = StoredValues.Open(keys, Locations.ContactSignature(personId), reader.GetFieldValue<byte[]>(8));
        if (version is < byte.MinValue or > byte.MaxValue
            || !ContactCode.TryAssemble((byte)version, identityKey, staticKey, OnionAddress.FromPublicKey(onionKey), name, signature, out ContactCode? code, out _))
        {
            throw StoredValues.Inconsistent("a contact's stored parts do not make a valid contact code");
        }

        ContactChannel addedVia = reader.GetInt64(9) switch
        {
            1 => ContactChannel.InPerson,
            2 => ContactChannel.OtherChannel,
            _ => throw StoredValues.Inconsistent("a contact has an unknown channel"),
        };

        StoredContactStatus status = reader.GetInt64(10) switch
        {
            1 => StoredContactStatus.WaitingForFirstConnection,
            2 => StoredContactStatus.Confirmed,
            _ => throw StoredValues.Inconsistent("a contact has an unknown status"),
        };

        bool keyChecked = reader.GetInt64(11) switch
        {
            0 => false,
            1 => true,
            _ => throw StoredValues.Inconsistent("a contact has an unknown key check"),
        };

        return new StoredContact(code, addedVia, status, keyChecked);
    }

    private async Task<bool> SaveAsync(SqliteConnection connection, SqliteTransaction transaction, StoredContact contact, CancellationToken cancellationToken)
    {
        ContactCode code = contact.Code;
        byte[] identityLookup = keys.Lookup(LookupDomain.IdentityKey, code.IdentityKey.Bytes);
        long? personId = await Sql.LongAsync(connection, transaction, SelectPersonByLookup, cancellationToken, ("$identity_key_mac", identityLookup)).ConfigureAwait(false);

        if (personId is null)
        {
            personId = await Sql.LongAsync(connection, transaction, NextPersonId, cancellationToken).ConfigureAwait(false) ?? 1;
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertPerson,
                cancellationToken,
                ("$id", personId.Value),
                ("$identity_key_mac", identityLookup),
                ("$identity_key_enc", keys.Seal(Locations.PersonIdentityKey(personId.Value), code.IdentityKey.Bytes)),
                ("$display_name_enc", keys.Seal(Locations.PersonName(personId.Value), StoredValues.Utf8(code.DisplayName.Value)))).ConfigureAwait(false);
        }
        else
        {
            if (await Sql.LongAsync(connection, transaction, SelectOwnPerson, cancellationToken).ConfigureAwait(false) == personId)
            {
                throw new InvalidOperationException("A contact cannot have the user's own identity key.");
            }

            await Sql.ExecuteAsync(
                connection,
                transaction,
                UpdatePersonName,
                cancellationToken,
                ("$id", personId.Value),
                ("$display_name_enc", keys.Seal(Locations.PersonName(personId.Value), StoredValues.Utf8(code.DisplayName.Value)))).ConfigureAwait(false);
        }

        long person = personId.Value;
        await Sql.ExecuteAsync(
            connection,
            transaction,
            UpsertContact,
            cancellationToken,
            ("$person_id", person),
            ("$code_version", (long)ContactCode.Version),
            ("$static_key_mac", keys.Lookup(LookupDomain.StaticKey, code.StaticKey.Bytes)),
            ("$static_key_enc", keys.Seal(Locations.ContactStaticKey(person), code.StaticKey.Bytes)),
            ("$onion_key_enc", keys.Seal(Locations.ContactOnionKey(person), code.OnionAddress.PublicKey.Bytes)),
            ("$code_signature_enc", keys.Seal(Locations.ContactSignature(person), code.Signature)),
            ("$added_via", (long)contact.AddedVia),
            ("$status", (long)contact.Status),
            ("$key_checked", contact.KeyChecked ? 1L : 0L)).ConfigureAwait(false);

        return true;
    }
}
