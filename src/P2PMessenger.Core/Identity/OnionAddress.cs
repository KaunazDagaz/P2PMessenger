using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Identity;

public sealed class OnionAddress : IEquatable<OnionAddress>
{
    public const int LabelLength = 56;
    public const int Length = Ed25519PublicKey.Length + ChecksumLength + 1;

    private const byte Version = 3;
    private const int ChecksumLength = 2;
    private const string Suffix = ".onion";

    private readonly byte[] _bytes;

    private OnionAddress(byte[] bytes, Ed25519PublicKey publicKey)
    {
        _bytes = bytes;
        PublicKey = publicKey;
        Label = Base32.Encode(bytes, lowerCase: true);
    }

    public Ed25519PublicKey PublicKey { get; }

    public string Label { get; }

    public string Host => Label + Suffix;

    public ReadOnlySpan<byte> Bytes => _bytes;

    private static ReadOnlySpan<byte> ChecksumLabel => ".onion checksum"u8;

    public static OnionAddress FromPublicKey(Ed25519PublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        byte[] bytes = new byte[Length];
        publicKey.Bytes.CopyTo(bytes);
        Checksum(publicKey.Bytes, bytes.AsSpan(Ed25519PublicKey.Length, ChecksumLength));
        bytes[^1] = Version;

        return new OnionAddress(bytes, publicKey);
    }

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out OnionAddress? address)
    {
        address = null;

        if (bytes.Length != Length
            || bytes[^1] != Version
            || !Ed25519PublicKey.TryCreate(bytes[..Ed25519PublicKey.Length], out Ed25519PublicKey? publicKey))
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[ChecksumLength];
        Checksum(publicKey.Bytes, expected);
        if (!bytes.Slice(Ed25519PublicKey.Length, ChecksumLength).SequenceEqual(expected))
        {
            return false;
        }

        address = new OnionAddress(bytes.ToArray(), publicKey);
        return true;
    }

    public bool Equals(OnionAddress? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is OnionAddress other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    private static void Checksum(ReadOnlySpan<byte> publicKey, Span<byte> checksum)
    {
        Span<byte> input = stackalloc byte[ChecksumLabel.Length + Ed25519PublicKey.Length + 1];
        ChecksumLabel.CopyTo(input);
        publicKey.CopyTo(input[ChecksumLabel.Length..]);
        input[^1] = Version;
        Digests.Sha3256(input).AsSpan(0, ChecksumLength).CopyTo(checksum);
    }
}
