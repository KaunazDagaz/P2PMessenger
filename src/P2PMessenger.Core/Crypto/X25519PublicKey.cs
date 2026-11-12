using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Crypto;

public sealed class X25519PublicKey : IEquatable<X25519PublicKey>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private X25519PublicKey(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => _bytes;

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out X25519PublicKey? key)
    {
        if (bytes.Length != Length || !IsCanonical(bytes))
        {
            key = null;
            return false;
        }

        key = new X25519PublicKey(bytes.ToArray());
        return true;
    }

    public bool Equals(X25519PublicKey? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is X25519PublicKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    internal static X25519PublicKey FromTrustedBytes(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    internal bool IsUsable()
    {
        if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, _bytes, KeyBlobFormat.RawPublicKey, out PublicKey? other) || other is null)
        {
            return false;
        }

        using Key probe = Key.Create(KeyAgreementAlgorithm.X25519);
        using SharedSecret? secret = KeyAgreementAlgorithm.X25519.Agree(probe, other);
        return secret is not null;
    }

    private static bool IsCanonical(ReadOnlySpan<byte> bytes)
    {
        if ((bytes[Length - 1] & 0x80) != 0)
        {
            return false;
        }

        if (bytes[Length - 1] != 0x7F || bytes[0] < 0xED)
        {
            return true;
        }

        return bytes[1..(Length - 1)].ContainsAnyExcept((byte)0xFF);
    }
}
