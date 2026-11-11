using NSec.Cryptography;

namespace P2PMessenger.Spikes.Nearby.Identification;

public sealed class Contact(string name, byte[] publicKey, Key pairwiseMacKey, byte[]? cachedStaticDh) : IDisposable
{
    public string Name { get; } = name;

    public byte[] PublicKey { get; } = publicKey;

    public Key PairwiseMacKey { get; } = pairwiseMacKey;

    public byte[]? CachedStaticDh { get; } = cachedStaticDh;

    public void Dispose()
    {
        PairwiseMacKey.Dispose();
        if (CachedStaticDh is not null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(CachedStaticDh);
        }
    }
}
