using System.Security.Cryptography;

namespace P2PMessenger.Spikes.Tor;

public sealed class OnionServiceKey : IDisposable
{
    public const int SeedLength = 32;
    private const int ExpandedLength = 64;

    private readonly byte[] _seed;

    private OnionServiceKey(byte[] seed)
    {
        _seed = seed;
    }

    public static OnionServiceKey Create() => new(RandomNumberGenerator.GetBytes(SeedLength));

    public static OnionServiceKey FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != SeedLength)
        {
            throw new ArgumentException($"An onion service seed has {SeedLength} bytes.", nameof(seed));
        }

        return new OnionServiceKey(seed.ToArray());
    }

    public void ExportSeed(Span<byte> destination) => _seed.CopyTo(destination);

    public void Dispose() => CryptographicOperations.ZeroMemory(_seed);

    internal string ToAddOnionKeyArgument()
    {
        Span<byte> expanded = stackalloc byte[ExpandedLength];
        try
        {
            SHA512.HashData(_seed, expanded);
            expanded[0] &= 248;
            expanded[31] &= 127;
            expanded[31] |= 64;
            return "ED25519-V3:" + Convert.ToBase64String(expanded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expanded);
        }
    }
}
