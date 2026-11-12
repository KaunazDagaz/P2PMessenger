using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Identity;

public sealed class Fingerprint : IEquatable<Fingerprint>
{
    public const int DigitCount = 30;
    public const int GroupSize = 5;

    private const int GroupCount = DigitCount / GroupSize;
    private const ulong GroupModulus = 100_000;

    private Fingerprint(string digits)
    {
        Digits = digits;
    }

    public string Digits { get; }

    private static ReadOnlySpan<byte> Label => "P2PM/fingerprint/v1"u8;

    public static Fingerprint Of(Ed25519PublicKey identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        Span<byte> input = stackalloc byte[Label.Length + Ed25519PublicKey.Length];
        Label.CopyTo(input);
        identityKey.Bytes.CopyTo(input[Label.Length..]);
        byte[] hash = Digests.Blake2b256(input);

        Span<char> digits = stackalloc char[DigitCount];
        for (int group = 0; group < GroupCount; group++)
        {
            ulong chunk = 0;
            foreach (byte value in hash.AsSpan(group * GroupSize, GroupSize))
            {
                chunk = (chunk << 8) | value;
            }

            uint remaining = (uint)(chunk % GroupModulus);
            for (int position = GroupSize - 1; position >= 0; position--)
            {
                digits[(group * GroupSize) + position] = (char)('0' + (remaining % 10));
                remaining /= 10;
            }
        }

        return new Fingerprint(new string(digits));
    }

    public string ToGroupedString()
    {
        var groups = new string[GroupCount];
        for (int group = 0; group < GroupCount; group++)
        {
            groups[group] = Digits.Substring(group * GroupSize, GroupSize);
        }

        return string.Join(' ', groups);
    }

    public bool Equals(Fingerprint? other) => other is not null && string.Equals(Digits, other.Digits, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Fingerprint other && Equals(other);

    public override int GetHashCode() => Digits.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => ToGroupedString();
}
