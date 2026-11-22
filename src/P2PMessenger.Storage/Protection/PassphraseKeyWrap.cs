using System.Buffers.Binary;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace P2PMessenger.Storage.Protection;

public static class PassphraseKeyWrap
{
    public const int WrappedLength = 1 + SealedLength;
    public const int ShareLength = 32;
    public const int MaxProtectedShareLength = 4096;

    private const byte PassphraseOnly = 1;
    private const byte WithKeyStore = 2;
    private const int NonceLength = 24;
    private const int TagLength = 16;
    private const int SealedLength = NonceLength + RecordKeys.DataKeyLength + TagLength;
    private const int KeyStoreHeaderLength = 4;
    private const int ArgonLength = 32;

    private static ReadOnlySpan<byte> Context => "P2PM/data-key/passphrase/v1"u8;

    private static ReadOnlySpan<byte> KeyStoreContext => "P2PM/data-key/passphrase/v2"u8;

    private static ReadOnlySpan<byte> KeyStoreKeyInfo => "P2PM/data-key/passphrase/v2/wrap-key"u8;

    public static WrappedDataKey Wrap(ReadOnlySpan<byte> dataKey, string passphrase, PassphraseKdf kdf)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        return Wrap(dataKey, passphrase, NewDerivation(kdf), nonce);
    }

    public static WrappedDataKey Wrap(
        ReadOnlySpan<byte> dataKey,
        string passphrase,
        PassphraseKdf kdf,
        DataKeyProtection keyStore,
        ReadOnlySpan<byte> share,
        ReadOnlySpan<byte> protectedShare)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        return Wrap(dataKey, passphrase, NewDerivation(kdf), keyStore, share, protectedShare, nonce);
    }

    public static PassphraseUnwrapOutcome TryUnwrap(WrappedDataKey wrapped, string passphrase, Span<byte> dataKey) =>
        TryUnwrap(wrapped, passphrase, default, dataKey);

    public static PassphraseUnwrapOutcome TryUnwrap(WrappedDataKey wrapped, string passphrase, ReadOnlySpan<byte> share, Span<byte> dataKey)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        ArgumentNullException.ThrowIfNull(passphrase);
        if (wrapped.Derivation is not { } derivation)
        {
            throw new ArgumentException("The data key is not wrapped by a passphrase.", nameof(wrapped));
        }

        if (dataKey.Length != RecordKeys.DataKeyLength)
        {
            throw new ArgumentException("The data key buffer must be 32 bytes long.", nameof(dataKey));
        }

        ReadOnlySpan<byte> value = wrapped.WrappedKey.Span;
        if (!IsWellFormed(value))
        {
            return PassphraseUnwrapOutcome.Damaged;
        }

        bool withKeyStore = value[0] == WithKeyStore;
        if (share.Length != (withKeyStore ? ShareLength : 0))
        {
            throw new ArgumentException(withKeyStore ? "The key store's share must be 32 bytes long." : "A data key wrapped by the passphrase alone takes no share.", nameof(share));
        }

        if (!PassphraseText.TryNormalize(passphrase, out _))
        {
            return PassphraseUnwrapOutcome.WrongPassphrase;
        }

        ReadOnlySpan<byte> sealedKey = value[^SealedLength..];
        using Key key = withKeyStore ? DeriveKey(passphrase, derivation, share) : DeriveKey(passphrase, derivation);
        byte[] associatedData = withKeyStore ? KeyStoreAssociatedData(derivation, (DataKeyProtection)value[1]) : AssociatedData(derivation);
        return AeadAlgorithm.XChaCha20Poly1305.Decrypt(key, sealedKey[..NonceLength], associatedData, sealedKey[NonceLength..], dataKey)
            ? PassphraseUnwrapOutcome.Succeeded
            : PassphraseUnwrapOutcome.WrongPassphrase;
    }

    public static bool IsWellFormed(ReadOnlySpan<byte> wrappedKey)
    {
        if (wrappedKey.Length == WrappedLength && wrappedKey[0] == PassphraseOnly)
        {
            return true;
        }

        if (wrappedKey.Length < KeyStoreHeaderLength + 1 + SealedLength || wrappedKey[0] != WithKeyStore || !IsKeyStore(wrappedKey[1]))
        {
            return false;
        }

        int shareLength = BinaryPrimitives.ReadUInt16BigEndian(wrappedKey[2..]);
        return shareLength is > 0 and <= MaxProtectedShareLength && wrappedKey.Length == KeyStoreHeaderLength + shareLength + SealedLength;
    }

    public static bool TryGetKeyStoreShare(WrappedDataKey wrapped, out DataKeyProtection keyStore, out ReadOnlyMemory<byte> protectedShare)
    {
        ArgumentNullException.ThrowIfNull(wrapped);

        ReadOnlyMemory<byte> value = wrapped.WrappedKey;
        if (wrapped.Protection != DataKeyProtection.Passphrase || !IsWellFormed(value.Span) || value.Span[0] != WithKeyStore)
        {
            keyStore = default;
            protectedShare = default;
            return false;
        }

        keyStore = (DataKeyProtection)value.Span[1];
        protectedShare = value.Slice(KeyStoreHeaderLength, BinaryPrimitives.ReadUInt16BigEndian(value.Span[2..]));
        return true;
    }

    internal static WrappedDataKey Wrap(ReadOnlySpan<byte> dataKey, string passphrase, PassphraseDerivation derivation, ReadOnlySpan<byte> nonce)
    {
        CheckInputs(dataKey, passphrase, derivation, nonce);

        byte[] wrapped = new byte[WrappedLength];
        wrapped[0] = PassphraseOnly;
        using Key key = DeriveKey(passphrase, derivation);
        Seal(key, nonce, AssociatedData(derivation), dataKey, wrapped.AsSpan(1));
        return new WrappedDataKey(DataKeyProtection.Passphrase, wrapped, derivation);
    }

    internal static WrappedDataKey Wrap(
        ReadOnlySpan<byte> dataKey,
        string passphrase,
        PassphraseDerivation derivation,
        DataKeyProtection keyStore,
        ReadOnlySpan<byte> share,
        ReadOnlySpan<byte> protectedShare,
        ReadOnlySpan<byte> nonce)
    {
        CheckInputs(dataKey, passphrase, derivation, nonce);
        if (!IsKeyStore((byte)keyStore))
        {
            throw new ArgumentOutOfRangeException(nameof(keyStore), keyStore, "The share must be held by a key store.");
        }

        if (share.Length != ShareLength)
        {
            throw new ArgumentException("The key store's share must be 32 bytes long.", nameof(share));
        }

        if (protectedShare.IsEmpty || protectedShare.Length > MaxProtectedShareLength)
        {
            throw new ArgumentException("The protected share must hold 1 to 4096 bytes.", nameof(protectedShare));
        }

        byte[] wrapped = new byte[KeyStoreHeaderLength + protectedShare.Length + SealedLength];
        wrapped[0] = WithKeyStore;
        wrapped[1] = (byte)keyStore;
        BinaryPrimitives.WriteUInt16BigEndian(wrapped.AsSpan(2), (ushort)protectedShare.Length);
        protectedShare.CopyTo(wrapped.AsSpan(KeyStoreHeaderLength));
        using Key key = DeriveKey(passphrase, derivation, share);
        Seal(key, nonce, KeyStoreAssociatedData(derivation, keyStore), dataKey, wrapped.AsSpan(KeyStoreHeaderLength + protectedShare.Length));
        return new WrappedDataKey(DataKeyProtection.Passphrase, wrapped, derivation);
    }

    internal static byte[] AssociatedData(PassphraseDerivation derivation) =>
        AssociatedDataFor(Context, [PassphraseOnly], derivation);

    internal static byte[] KeyStoreAssociatedData(PassphraseDerivation derivation, DataKeyProtection keyStore) =>
        AssociatedDataFor(KeyStoreContext, [WithKeyStore, (byte)keyStore], derivation);

    private static byte[] AssociatedDataFor(ReadOnlySpan<byte> context, ReadOnlySpan<byte> format, PassphraseDerivation derivation)
    {
        byte[] data = new byte[context.Length + format.Length + PassphraseDerivation.SaltLength + 8];
        context.CopyTo(data);
        int offset = context.Length;
        format.CopyTo(data.AsSpan(offset));
        offset += format.Length;
        derivation.Salt.Span.CopyTo(data.AsSpan(offset));
        offset += PassphraseDerivation.SaltLength;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), checked((uint)derivation.Kdf.MemoryKib));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 4), checked((uint)derivation.Kdf.Passes));
        return data;
    }

    private static PassphraseDerivation NewDerivation(PassphraseKdf kdf)
    {
        Span<byte> salt = stackalloc byte[PassphraseDerivation.SaltLength];
        RandomNumberGenerator.Fill(salt);
        return new PassphraseDerivation(salt, kdf);
    }

    private static void CheckInputs(ReadOnlySpan<byte> dataKey, string passphrase, PassphraseDerivation derivation, ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(derivation);
        if (dataKey.Length != RecordKeys.DataKeyLength)
        {
            throw new ArgumentException("The data key must be 32 bytes long.", nameof(dataKey));
        }

        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException("The nonce must be 24 bytes long.", nameof(nonce));
        }
    }

    private static void Seal(Key key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> dataKey, Span<byte> destination)
    {
        nonce.CopyTo(destination);
        AeadAlgorithm.XChaCha20Poly1305.Encrypt(key, nonce, associatedData, dataKey, destination[NonceLength..]);
    }

    private static bool IsKeyStore(byte code) =>
        code is (byte)DataKeyProtection.WindowsDpapi or (byte)DataKeyProtection.AndroidKeystore or (byte)DataKeyProtection.LinuxSecretService;

    private static Argon2id Argon2For(PassphraseDerivation derivation)
    {
        Argon2Parameters parameters = new()
        {
            DegreeOfParallelism = 1,
            MemorySize = derivation.Kdf.MemoryKib,
            NumberOfPasses = derivation.Kdf.Passes,
        };
        return PasswordBasedKeyDerivationAlgorithm.Argon2id(in parameters);
    }

    private static Key DeriveKey(string passphrase, PassphraseDerivation derivation)
    {
        byte[] password = PassphraseText.Encode(passphrase);
        try
        {
            return Argon2For(derivation).DeriveKey(password, derivation.Salt.Span, AeadAlgorithm.XChaCha20Poly1305);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    private static Key DeriveKey(string passphrase, PassphraseDerivation derivation, ReadOnlySpan<byte> share)
    {
        byte[] password = PassphraseText.Encode(passphrase);
        byte[] material = new byte[ArgonLength + ShareLength];
        byte[]? pseudorandomKey = null;
        try
        {
            Argon2For(derivation).DeriveBytes(password, derivation.Salt.Span, material.AsSpan(0, ArgonLength));
            share.CopyTo(material.AsSpan(ArgonLength));
            HkdfSha256 hkdf = KeyDerivationAlgorithm.HkdfSha256;
            pseudorandomKey = hkdf.Extract(material, KeyStoreContext);
            return hkdf.ExpandKey(pseudorandomKey, KeyStoreKeyInfo, AeadAlgorithm.XChaCha20Poly1305);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            CryptographicOperations.ZeroMemory(material);
            if (pseudorandomKey is not null)
            {
                CryptographicOperations.ZeroMemory(pseudorandomKey);
            }
        }
    }
}
