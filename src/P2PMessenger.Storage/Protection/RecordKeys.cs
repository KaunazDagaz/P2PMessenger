using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace P2PMessenger.Storage.Protection;

public sealed class RecordKeys : IDisposable
{
    public const int DataKeyLength = 32;
    public const int NonceLength = 24;
    public const int TagLength = 16;
    public const int Overhead = NonceLength + TagLength;
    public const int LookupLength = 32;

    private readonly Key recordKey;
    private readonly Key lookupKey;

    private RecordKeys(Key recordKey, Key lookupKey)
    {
        this.recordKey = recordKey;
        this.lookupKey = lookupKey;
    }

    private static ReadOnlySpan<byte> DerivationSalt => "P2PM/storage/v1"u8;

    private static ReadOnlySpan<byte> RecordKeyInfo => "P2PM/storage/v1/record-key"u8;

    private static ReadOnlySpan<byte> LookupKeyInfo => "P2PM/storage/v1/lookup-key"u8;

    public static RecordKeys Derive(ReadOnlySpan<byte> dataKey)
    {
        if (dataKey.Length != DataKeyLength)
        {
            throw new ArgumentException("The data key must be 32 bytes long.", nameof(dataKey));
        }

        HkdfSha256 hkdf = KeyDerivationAlgorithm.HkdfSha256;
        byte[] pseudorandomKey = hkdf.Extract(dataKey, DerivationSalt);
        Key? recordKey = null;
        try
        {
            recordKey = hkdf.ExpandKey(pseudorandomKey, RecordKeyInfo, AeadAlgorithm.XChaCha20Poly1305);
            Key lookupKey = hkdf.ExpandKey(pseudorandomKey, LookupKeyInfo, MacAlgorithm.Blake2b_256);
            return new RecordKeys(recordKey, lookupKey);
        }
        catch
        {
            recordKey?.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudorandomKey);
        }
    }

    public byte[] Seal(RecordLocation location, ReadOnlySpan<byte> plaintext)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);

        byte[] sealedValue = new byte[Overhead + plaintext.Length];
        nonce.CopyTo(sealedValue);
        AeadAlgorithm.XChaCha20Poly1305.Encrypt(recordKey, nonce, location.AssociatedData(), plaintext, sealedValue.AsSpan(NonceLength));
        return sealedValue;
    }

    public bool TryOpen(RecordLocation location, ReadOnlySpan<byte> sealedValue, [NotNullWhen(true)] out byte[]? plaintext)
    {
        plaintext = null;
        if (sealedValue.Length < Overhead)
        {
            return false;
        }

        byte[] opened = new byte[sealedValue.Length - Overhead];
        if (!AeadAlgorithm.XChaCha20Poly1305.Decrypt(recordKey, sealedValue[..NonceLength], location.AssociatedData(), sealedValue[NonceLength..], opened))
        {
            return false;
        }

        plaintext = opened;
        return true;
    }

    public byte[] Lookup(LookupDomain domain, ReadOnlySpan<byte> value)
    {
        ReadOnlySpan<byte> label = LabelOf(domain);
        byte[] input = new byte[label.Length + 1 + value.Length];
        label.CopyTo(input);
        value.CopyTo(input.AsSpan(label.Length + 1));
        return MacAlgorithm.Blake2b_256.Mac(lookupKey, input);
    }

    public bool MatchesLookup(LookupDomain domain, ReadOnlySpan<byte> value, ReadOnlySpan<byte> stored) =>
        stored.Length == LookupLength && CryptographicOperations.FixedTimeEquals(Lookup(domain, value), stored);

    public void Dispose()
    {
        recordKey.Dispose();
        lookupKey.Dispose();
    }

    private static ReadOnlySpan<byte> LabelOf(LookupDomain domain) => domain switch
    {
        LookupDomain.IdentityKey => "P2PM/lookup/v1/identity-key"u8,
        LookupDomain.StaticKey => "P2PM/lookup/v1/static-key"u8,
        LookupDomain.MessageId => "P2PM/lookup/v1/message-id"u8,
        LookupDomain.GroupId => "P2PM/lookup/v1/group-id"u8,
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown lookup domain."),
    };
}
