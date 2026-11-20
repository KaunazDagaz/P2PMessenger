using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class IdentityRepository
{
    private const string SelectIdentity =
        "SELECT identity.person_id, identity.key_set_enc, person.identity_key_mac, person.identity_key_enc, person.display_name_enc "
        + "FROM identity JOIN person ON person.id = identity.person_id WHERE identity.id = 1;";

    private const string CountIdentities = "SELECT count(*) FROM identity;";
    private const string SelectPersonByLookup = "SELECT id FROM person WHERE identity_key_mac = $identity_key_mac;";
    private const string NextPersonId = "SELECT coalesce(max(id), 0) + 1 FROM person;";

    private const string InsertPerson =
        "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) "
        + "VALUES ($id, $identity_key_mac, $identity_key_enc, $display_name_enc);";

    private const string InsertIdentity = "INSERT INTO identity (id, person_id, key_set_enc) VALUES (1, $person_id, $key_set_enc);";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal IdentityRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<StoredIdentity?> LoadAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAsync, cancellationToken);

    public Task<bool> TryCreateAsync(KeySet keySet, DisplayName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keySet);
        ArgumentNullException.ThrowIfNull(name);

        return gate.WriteAsync((connection, transaction, token) => CreateAsync(connection, transaction, keySet, name, token), cancellationToken);
    }

    private async Task<StoredIdentity?> LoadAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Sql.Command(connection, null, SelectIdentity);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        long personId = reader.GetInt64(0);
        byte[] exported = StoredValues.Open(keys, Locations.IdentityKeySet, reader.GetFieldValue<byte[]>(1));
        KeySet? keySet = null;
        try
        {
            if (!KeySet.TryImport(exported, out keySet))
            {
                throw StoredValues.Damaged(Locations.IdentityKeySet);
            }

            Ed25519PublicKey identityKey = StoredValues.IdentityKey(keys, personId, reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3));
            if (!identityKey.Equals(keySet.IdentityKey))
            {
                throw StoredValues.Inconsistent("the user's person row does not hold the user's identity key");
            }

            DisplayName name = StoredValues.Name(keys, personId, reader.GetFieldValue<byte[]>(4));
            return new StoredIdentity(keySet, name);
        }
        catch
        {
            keySet?.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }
    }

    private async Task<bool> CreateAsync(SqliteConnection connection, SqliteTransaction transaction, KeySet keySet, DisplayName name, CancellationToken cancellationToken)
    {
        if (await Sql.LongAsync(connection, transaction, CountIdentities, cancellationToken).ConfigureAwait(false) > 0)
        {
            return false;
        }

        byte[] identityLookup = keys.Lookup(LookupDomain.IdentityKey, keySet.IdentityKey.Bytes);
        if (await Sql.LongAsync(connection, transaction, SelectPersonByLookup, cancellationToken, ("$identity_key_mac", identityLookup)).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("Another person in the database already has the identity key of the new identity.");
        }

        long personId = await Sql.LongAsync(connection, transaction, NextPersonId, cancellationToken).ConfigureAwait(false) ?? 1;
        await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertPerson,
            cancellationToken,
            ("$id", personId),
            ("$identity_key_mac", identityLookup),
            ("$identity_key_enc", keys.Seal(Locations.PersonIdentityKey(personId), keySet.IdentityKey.Bytes)),
            ("$display_name_enc", keys.Seal(Locations.PersonName(personId), StoredValues.Utf8(name.Value)))).ConfigureAwait(false);

        byte[] exported = new byte[KeySet.ExportLength];
        try
        {
            if (!keySet.TryExport(exported, out int written) || written != KeySet.ExportLength)
            {
                throw new InvalidOperationException("The identity's keys could not be exported for storage.");
            }

            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertIdentity,
                cancellationToken,
                ("$person_id", personId),
                ("$key_set_enc", keys.Seal(Locations.IdentityKeySet, exported))).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }

        return true;
    }
}
