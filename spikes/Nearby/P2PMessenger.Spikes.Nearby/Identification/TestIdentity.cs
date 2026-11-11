using System.Text;
using NSec.Cryptography;

namespace P2PMessenger.Spikes.Nearby.Identification;

public sealed class TestIdentity : IDisposable
{
    private const string SeedPrefix = "P2PM/s05/test-identity/";

    private readonly byte[] _publicKey;

    private TestIdentity(string name, Key staticKey)
    {
        Name = name;
        StaticKey = staticKey;
        _publicKey = staticKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
    }

    public string Name { get; }

    public Key StaticKey { get; }

    public ReadOnlySpan<byte> PublicKey => _publicKey;

    public static TestIdentity FromName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        byte[] seed = HashAlgorithm.Blake2b_256.Hash(Encoding.UTF8.GetBytes(SeedPrefix + name));
        return new TestIdentity(name, Key.Import(KeyAgreementAlgorithm.X25519, seed, KeyBlobFormat.RawPrivateKey));
    }

    public static TestIdentity CreateRandom(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new TestIdentity(name, Key.Create(KeyAgreementAlgorithm.X25519));
    }

    public void Dispose() => StaticKey.Dispose();
}
