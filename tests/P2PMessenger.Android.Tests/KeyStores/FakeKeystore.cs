using System.Security.Cryptography;
using NSec.Cryptography;
using P2PMessenger.Android.KeyStores;

namespace P2PMessenger.Android.Tests.KeyStores;

internal sealed class FakeKeystore : IKeystoreKeys
{
    private const int KeyLength = 32;
    private const int RealNonceLength = 12;

    private static readonly AeadAlgorithm Algorithm = AeadAlgorithm.ChaCha20Poly1305;

    private readonly Lock sync = new();
    private readonly Dictionary<string, (byte[] Key, bool StrongBox)> entries = new(StringComparer.Ordinal);

    public bool HasStrongBox { get; init; }

    public KeystoreStatus StrongBoxCreate { get; set; } = KeystoreStatus.Succeeded;

    public KeystoreStatus TeeCreate { get; set; } = KeystoreStatus.Succeeded;

    public KeystoreStatus? EncryptAnswer { get; set; }

    public KeystoreStatus? DecryptAnswer { get; set; }

    public int NonceLength { get; set; } = RealNonceLength;

    public int ExtraCiphertextBytes { get; set; }

    public int? PlaintextLength { get; set; }

    public List<(string Alias, bool StrongBox)> Creates { get; } = [];

    public List<string> Deletes { get; } = [];

    public int DecryptCalls { get; private set; }

    public byte[] LastAssociatedData { get; private set; } = [];

    public IReadOnlyList<string> Aliases
    {
        get
        {
            lock (sync)
            {
                return [.. entries.Keys.Order(StringComparer.Ordinal)];
            }
        }
    }

    public bool IsInStrongBox(string alias)
    {
        lock (sync)
        {
            return entries[alias].StrongBox;
        }
    }

    public KeystoreStatus Create(string alias, bool strongBox)
    {
        lock (sync)
        {
            Creates.Add((alias, strongBox));
            var answer = strongBox ? StrongBoxCreate : TeeCreate;
            if (answer == KeystoreStatus.Succeeded)
            {
                entries[alias] = (RandomNumberGenerator.GetBytes(KeyLength), strongBox);
            }

            return answer;
        }
    }

    public KeystoreStatus Encrypt(string alias, byte[] associatedData, byte[] plaintext, out byte[] nonce, out byte[] ciphertext)
    {
        lock (sync)
        {
            nonce = [];
            ciphertext = [];
            LastAssociatedData = [.. associatedData];
            if (EncryptAnswer is { } answer)
            {
                return answer;
            }

            if (!entries.TryGetValue(alias, out var entry))
            {
                return KeystoreStatus.NotFound;
            }

            var realNonce = RandomNumberGenerator.GetBytes(RealNonceLength);
            using var key = Key.Import(Algorithm, entry.Key, KeyBlobFormat.RawSymmetricKey);
            var sealedBytes = Algorithm.Encrypt(key, realNonce, associatedData, plaintext);
            nonce = NonceLength == RealNonceLength ? realNonce : RandomNumberGenerator.GetBytes(NonceLength);
            ciphertext = [.. sealedBytes, .. new byte[ExtraCiphertextBytes]];
            return KeystoreStatus.Succeeded;
        }
    }

    public KeystoreStatus Decrypt(string alias, byte[] nonce, byte[] associatedData, byte[] ciphertext, out byte[] plaintext)
    {
        lock (sync)
        {
            DecryptCalls++;
            plaintext = [];
            if (DecryptAnswer is { } answer)
            {
                return answer;
            }

            if (!entries.TryGetValue(alias, out var entry))
            {
                return KeystoreStatus.NotFound;
            }

            if (nonce.Length != RealNonceLength || ciphertext.Length < Algorithm.TagSize)
            {
                return KeystoreStatus.Rejected;
            }

            using var key = Key.Import(Algorithm, entry.Key, KeyBlobFormat.RawSymmetricKey);
            var opened = Algorithm.Decrypt(key, nonce, associatedData, ciphertext);
            if (opened is null)
            {
                return KeystoreStatus.Rejected;
            }

            if (PlaintextLength is { } length)
            {
                var resized = new byte[length];
                opened.AsSpan(0, Math.Min(length, opened.Length)).CopyTo(resized);
                plaintext = resized;
            }
            else
            {
                plaintext = opened;
            }

            return KeystoreStatus.Succeeded;
        }
    }

    public void Delete(string alias)
    {
        lock (sync)
        {
            Deletes.Add(alias);
            _ = entries.Remove(alias);
        }
    }
}
