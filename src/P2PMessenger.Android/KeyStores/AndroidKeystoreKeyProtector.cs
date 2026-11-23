using System.Security.Cryptography;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Android.KeyStores;

internal sealed class AndroidKeystoreKeyProtector : IKeyProtector
{
    public const int KeyIdLength = 16;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int DataKeyLength = 32;
    public const int ProtectedKeyLength = 1 + KeyIdLength + NonceLength + DataKeyLength + TagLength;
    public const string AliasPrefix = "p2pm-data-key-";

    private const byte FormatVersion = 1;
    private const int NonceOffset = 1 + KeyIdLength;
    private const int CiphertextOffset = NonceOffset + NonceLength;

    private readonly IKeystoreKeys keys;

    public AndroidKeystoreKeyProtector(IKeystoreKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        this.keys = keys;
    }

    public KeyProtection Protection => KeyProtection.AndroidKeystore;

    private static ReadOnlySpan<byte> Context => "P2PM/data-key/android-keystore/v1"u8;

    public static string Alias(ReadOnlySpan<byte> keyId)
    {
        if (keyId.Length != KeyIdLength)
        {
            throw new ArgumentException("A key ID is 16 bytes long.", nameof(keyId));
        }

        return AliasPrefix + Convert.ToHexStringLower(keyId);
    }

    public static byte[] AssociatedData(ReadOnlySpan<byte> keyId) => [.. Context, FormatVersion, .. keyId];

    public async Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
    {
        if (dataKey.Length != DataKeyLength)
        {
            throw new ArgumentException("The data key must be 32 bytes long.", nameof(dataKey));
        }

        byte[] plain = dataKey.ToArray();
        try
        {
            return await Task.Run(() => Protect(plain), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public async Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
    {
        if (dataKey.Length != DataKeyLength)
        {
            throw new ArgumentException("The data key buffer must be 32 bytes long.", nameof(dataKey));
        }

        if (!IsWellFormed(protectedKey.Span))
        {
            return KeyStoreOutcome.KeyNotFound;
        }

        byte[] stored = protectedKey.ToArray();
        (KeystoreStatus status, byte[] plain) = await Task.Run(() => Open(stored), cancellationToken).ConfigureAwait(false);
        try
        {
            if (status == KeystoreStatus.Succeeded && plain.Length == DataKeyLength)
            {
                plain.CopyTo(dataKey);
                return KeyStoreOutcome.Succeeded;
            }

            return status == KeystoreStatus.Unavailable ? KeyStoreOutcome.Unavailable : KeyStoreOutcome.KeyNotFound;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task ForgetAsync(ReadOnlyMemory<byte> protectedKey, CancellationToken cancellationToken)
    {
        if (!IsWellFormed(protectedKey.Span))
        {
            return Task.CompletedTask;
        }

        string alias = Alias(protectedKey.Span.Slice(1, KeyIdLength));
        return Task.Run(() => keys.Delete(alias), cancellationToken);
    }

    private static bool IsWellFormed(ReadOnlySpan<byte> protectedKey) =>
        protectedKey.Length == ProtectedKeyLength && protectedKey[0] == FormatVersion;

    private ProtectResult Protect(byte[] dataKey)
    {
        byte[] keyId = RandomNumberGenerator.GetBytes(KeyIdLength);
        string alias = Alias(keyId);

        KeystoreStatus created = KeystoreStatus.StrongBoxUnavailable;
        if (keys.HasStrongBox)
        {
            created = keys.Create(alias, strongBox: true);
            if (created != KeystoreStatus.Succeeded)
            {
                keys.Delete(alias);
            }
        }

        if (created != KeystoreStatus.Succeeded)
        {
            created = keys.Create(alias, strongBox: false);
        }

        if (created != KeystoreStatus.Succeeded)
        {
            keys.Delete(alias);
            return ProtectResult.Failure(KeyStoreOutcome.Unavailable);
        }

        KeystoreStatus encrypted = keys.Encrypt(alias, AssociatedData(keyId), dataKey, out byte[] nonce, out byte[] ciphertext);
        if (encrypted != KeystoreStatus.Succeeded || nonce.Length != NonceLength || ciphertext.Length != DataKeyLength + TagLength)
        {
            keys.Delete(alias);
            return ProtectResult.Failure(KeyStoreOutcome.Unavailable);
        }

        byte[] protectedKey = new byte[ProtectedKeyLength];
        protectedKey[0] = FormatVersion;
        keyId.CopyTo(protectedKey, 1);
        nonce.CopyTo(protectedKey, NonceOffset);
        ciphertext.CopyTo(protectedKey, CiphertextOffset);
        return ProtectResult.Success(protectedKey);
    }

    private (KeystoreStatus Status, byte[] Plaintext) Open(byte[] stored)
    {
        ReadOnlySpan<byte> keyId = stored.AsSpan(1, KeyIdLength);
        KeystoreStatus status = keys.Decrypt(
            Alias(keyId),
            stored[NonceOffset..CiphertextOffset],
            AssociatedData(keyId),
            stored[CiphertextOffset..],
            out byte[] plaintext);
        return (status, plaintext);
    }
}
