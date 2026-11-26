using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Copy;

public sealed class CopyPairingCode : IEquatable<CopyPairingCode>
{
    public const byte Version = 1;
    public const string TextPrefix = "P2PMC1-";
    public const int SecretLength = 32;
    public const int MaxAddresses = 8;

    internal const int ReceiverKeyOffset = 1;
    internal const int SecretOffset = ReceiverKeyOffset + X25519PublicKey.Length;
    internal const int PortOffset = SecretOffset + SecretLength;
    internal const int AddressCountOffset = PortOffset + sizeof(ushort);
    internal const int AddressesOffset = AddressCountOffset + 1;
    internal const int MinLength = AddressesOffset + Ipv4Address.Length;
    internal const int MaxLength = AddressesOffset + (MaxAddresses * Ipv4Address.Length);

    private const string TextMarker = "P2PMC";
    private const char TextVersionDigit = '1';
    private const int TextMarkerLength = 5;
    private const int MaxTextInputLength = 4096;
    private const int MaxCompactTextLength = TextMarkerLength + 1 + (((MaxLength * 8) + 4) / 5);

    private readonly byte[] bytes;
    private readonly Ipv4Address[] addresses;

    private CopyPairingCode(byte[] bytes, X25519PublicKey receiverPublicKey, int port, Ipv4Address[] addresses)
    {
        this.bytes = bytes;
        ReceiverPublicKey = receiverPublicKey;
        Port = port;
        this.addresses = addresses;
    }

    public X25519PublicKey ReceiverPublicKey { get; }

    public ReadOnlySpan<byte> PairingSecret => bytes.AsSpan(SecretOffset, SecretLength);

    public int Port { get; }

    public IReadOnlyList<Ipv4Address> Addresses => addresses;

    public static CopyPairingCode Create(X25519PublicKey receiverPublicKey, ReadOnlySpan<byte> pairingSecret, int port, IReadOnlyCollection<Ipv4Address> addresses)
    {
        ArgumentNullException.ThrowIfNull(receiverPublicKey);
        ArgumentNullException.ThrowIfNull(addresses);
        if (!receiverPublicKey.IsUsable())
        {
            throw new ArgumentException("The receiver public key is not usable.", nameof(receiverPublicKey));
        }

        if (pairingSecret.Length != SecretLength || !pairingSecret.ContainsAnyExcept((byte)0))
        {
            throw new ArgumentException("The pairing secret must be 32 non-zero bytes.", nameof(pairingSecret));
        }

        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        Ipv4Address[] ordered = [.. addresses.Distinct().OrderBy(static item => item)];
        if (ordered.Length is 0 or > MaxAddresses)
        {
            throw new ArgumentException("A pairing code needs between 1 and 8 distinct local IPv4 addresses.", nameof(addresses));
        }

        foreach (Ipv4Address address in ordered)
        {
            if (address.Value == 0 || !Ipv4Address.TryCreate(address.A, address.B, address.C, address.D, out _))
            {
                throw new ArgumentException("A pairing code address is not dialable.", nameof(addresses));
            }
        }

        byte[] encoded = new byte[AddressesOffset + (ordered.Length * Ipv4Address.Length)];
        encoded[0] = Version;
        receiverPublicKey.Bytes.CopyTo(encoded.AsSpan(ReceiverKeyOffset));
        pairingSecret.CopyTo(encoded.AsSpan(SecretOffset));
        BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(PortOffset), (ushort)port);
        encoded[AddressCountOffset] = (byte)ordered.Length;
        for (int index = 0; index < ordered.Length; index++)
        {
            ordered[index].WriteTo(encoded.AsSpan(AddressesOffset + (index * Ipv4Address.Length)));
        }

        return new CopyPairingCode(encoded, receiverPublicKey, port, ordered);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out CopyPairingCode? code, out CopyPairingCodeError error)
    {
        code = null;
        error = CopyPairingCodeError.Damaged;
        if (bytes.IsEmpty)
        {
            return false;
        }

        if (bytes[0] != Version)
        {
            error = CopyPairingCodeError.UnsupportedVersion;
            return false;
        }

        if (bytes.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        int addressCount = bytes[AddressCountOffset];
        if (addressCount is 0 or > MaxAddresses || bytes.Length != AddressesOffset + (addressCount * Ipv4Address.Length))
        {
            return false;
        }

        int port = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(PortOffset, sizeof(ushort)));
        ReadOnlySpan<byte> secret = bytes.Slice(SecretOffset, SecretLength);
        if (port == 0
            || !secret.ContainsAnyExcept((byte)0)
            || !X25519PublicKey.TryCreate(bytes.Slice(ReceiverKeyOffset, X25519PublicKey.Length), out X25519PublicKey? receiverPublicKey)
            || !receiverPublicKey.IsUsable())
        {
            return false;
        }

        Ipv4Address[] parsed = new Ipv4Address[addressCount];
        for (int index = 0; index < addressCount; index++)
        {
            if (!Ipv4Address.TryCreate(bytes.Slice(AddressesOffset + (index * Ipv4Address.Length), Ipv4Address.Length), out Ipv4Address address))
            {
                return false;
            }

            if (index > 0 && address <= parsed[index - 1])
            {
                return false;
            }

            parsed[index] = address;
        }

        code = new CopyPairingCode(bytes.ToArray(), receiverPublicKey, port, parsed);
        error = CopyPairingCodeError.None;
        return true;
    }

    public static bool TryParseText(string? text, [NotNullWhen(true)] out CopyPairingCode? code, out CopyPairingCodeError error)
    {
        if (text is null)
        {
            code = null;
            error = CopyPairingCodeError.NotAPairingCode;
            return false;
        }

        return TryParseText(text.AsSpan(), out code, out error);
    }

    public static bool TryParseText(ReadOnlySpan<char> text, [NotNullWhen(true)] out CopyPairingCode? code, out CopyPairingCodeError error)
    {
        code = null;
        error = CopyPairingCodeError.NotAPairingCode;
        if (text.Length > MaxTextInputLength)
        {
            return false;
        }

        Span<char> compact = stackalloc char[MaxCompactTextLength];
        int length = 0;
        foreach (char c in text)
        {
            if (IsIgnorable(c))
            {
                continue;
            }

            if (length == compact.Length)
            {
                return false;
            }

            compact[length++] = c;
        }

        compact = compact[..length];
        if (compact.Length <= TextMarkerLength || !StartsWithMarker(compact))
        {
            return false;
        }

        char versionDigit = compact[TextMarkerLength];
        if (versionDigit != TextVersionDigit)
        {
            if (char.IsAsciiDigit(versionDigit))
            {
                error = CopyPairingCodeError.UnsupportedVersion;
            }

            return false;
        }

        Span<byte> decoded = stackalloc byte[MaxLength];
        if (!Base32.TryDecode(compact[(TextMarkerLength + 1)..], decoded, out int written))
        {
            error = CopyPairingCodeError.Damaged;
            return false;
        }

        if (!TryParse(decoded[..written], out code, out error))
        {
            error = CopyPairingCodeError.Damaged;
            return false;
        }

        return true;
    }

    public byte[] ToBytes() => bytes.ToArray();

    public string ToText() => TextPrefix + Base32.Encode(bytes, lowerCase: false);

    public bool Equals(CopyPairingCode? other) => other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) => obj is CopyPairingCode other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    internal ReadOnlySpan<byte> RawBytes => bytes;

    private static bool IsIgnorable(char c) =>
        char.IsWhiteSpace(c)
        || c is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF'
        || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.DashPunctuation;

    private static bool StartsWithMarker(ReadOnlySpan<char> compact)
    {
        for (int index = 0; index < TextMarkerLength; index++)
        {
            char c = compact[index];
            if (c is >= 'a' and <= 'z')
            {
                c = (char)(c - 32);
            }

            if (c != TextMarker[index])
            {
                return false;
            }
        }

        return true;
    }
}
