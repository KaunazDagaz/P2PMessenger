using NSec.Cryptography;
using P2PMessenger.Spikes.Nearby.Noise;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Spikes.Nearby.Identification;

public readonly record struct TrialResult(Contact? Caller, int Trials, byte[]? Payload);

public static class TrialIdentifier
{
    private const int DhLength = KkHandshake.DhLength;
    private const int CipherKeyLength = 32;

    public static TrialResult IdentifyNaive(ContactBook book, ReadOnlySpan<byte> prologue, ReadOnlySpan<byte> message1)
    {
        ArgumentNullException.ThrowIfNull(book);
        byte[] payload = new byte[Math.Max(0, message1.Length - KkHandshake.MessageOverhead)];
        int trials = 0;
        foreach (Contact contact in book.Contacts)
        {
            trials++;
            using KkHandshake handshake = new(NoiseProtocol.KkBlake2b, initiator: false, prologue, book.Self.StaticKey, contact.PublicKey);
            if (handshake.TryReadMessage(message1, payload, out int length))
            {
                return new TrialResult(contact, trials, payload[..length]);
            }
        }

        return new TrialResult(null, trials, null);
    }

    public static TrialResult IdentifyCached(ContactBook book, ReadOnlySpan<byte> prologue, ReadOnlySpan<byte> message1)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (message1.Length < KkHandshake.MessageOverhead || message1.Length > KkHandshake.MaxMessageLength)
        {
            return new TrialResult(null, 0, null);
        }

        ReadOnlySpan<byte> remoteEphemeral = message1[..DhLength];
        if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, remoteEphemeral, KeyBlobFormat.RawPublicKey, out PublicKey? ephemeralKey) || ephemeralKey is null)
        {
            return new TrialResult(null, 0, null);
        }

        NoiseHashFunction hash = NoiseHashFunction.Blake2b;
        int hashLength = hash.HashLength;
        Span<byte> es = stackalloc byte[DhLength];
        Span<byte> initialHash = stackalloc byte[hashLength];
        Span<byte> afterPrologue = stackalloc byte[hashLength];
        Span<byte> hashA = stackalloc byte[hashLength];
        Span<byte> hashB = stackalloc byte[hashLength];
        Span<byte> hashC = stackalloc byte[hashLength];
        Span<byte> chainingKey1 = stackalloc byte[hashLength];
        Span<byte> unusedKey = stackalloc byte[hashLength];
        Span<byte> chainingKey2 = stackalloc byte[hashLength];
        Span<byte> messageKey = stackalloc byte[hashLength];
        Span<byte> nonce = stackalloc byte[12];
        nonce.Clear();
        byte[] payload = new byte[message1.Length - KkHandshake.MessageOverhead];
        try
        {
            if (!TryAgree(book.Self.StaticKey, ephemeralKey, es))
            {
                return new TrialResult(null, 0, null);
            }

            initialHash.Clear();
            NoiseProtocol.KkBlake2b.NameBytes.CopyTo(initialHash);
            hash.Hash(initialHash, prologue, afterPrologue);
            ReadOnlySpan<byte> ownPublicKey = book.Self.PublicKey;
            ReadOnlySpan<byte> ciphertext = message1[DhLength..];
            int trials = 0;
            foreach (Contact contact in book.Contacts)
            {
                trials++;
                byte[] staticDh = contact.CachedStaticDh ?? throw new InvalidOperationException("The contact book does not cache static key agreements.");
                hash.Hash(afterPrologue, contact.PublicKey, hashA);
                hash.Hash(hashA, ownPublicKey, hashB);
                hash.Hash(hashB, remoteEphemeral, hashC);
                hash.Hkdf(initialHash, es, chainingKey1, unusedKey);
                hash.Hkdf(chainingKey1, staticDh, chainingKey2, messageKey);
                using Key key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, messageKey[..CipherKeyLength], KeyBlobFormat.RawSymmetricKey);
                if (AeadAlgorithm.ChaCha20Poly1305.Decrypt(key, nonce, hashC, ciphertext, payload))
                {
                    return new TrialResult(contact, trials, payload);
                }
            }

            return new TrialResult(null, trials, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(es);
            CryptographicOperations.ZeroMemory(chainingKey1);
            CryptographicOperations.ZeroMemory(chainingKey2);
            CryptographicOperations.ZeroMemory(unusedKey);
            CryptographicOperations.ZeroMemory(messageKey);
        }
    }

    private static bool TryAgree(Key staticKey, PublicKey remoteEphemeral, Span<byte> output)
    {
        SharedSecretCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(staticKey, remoteEphemeral, in parameters);
        return sharedSecret is not null
            && sharedSecret.TryExport(SharedSecretBlobFormat.RawSharedSecret, output, out int written)
            && written == DhLength;
    }
}
