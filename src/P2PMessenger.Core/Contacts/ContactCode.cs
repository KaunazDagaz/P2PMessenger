using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Contacts;

public sealed class ContactCode : IEquatable<ContactCode>
{
    public const byte Version = 1;
    public const string TextPrefix = "P2PM1-";

    internal const int IdentityOffset = 1;
    internal const int StaticOffset = IdentityOffset + Ed25519PublicKey.Length;
    internal const int OnionOffset = StaticOffset + X25519PublicKey.Length;
    internal const int NameLengthOffset = OnionOffset + OnionAddress.Length;
    internal const int HeaderLength = NameLengthOffset + 1;
    internal const int SignatureLength = Ed25519KeyPair.SignatureLength;
    internal const int MinLength = HeaderLength + 1 + SignatureLength;
    internal const int MaxLength = HeaderLength + DisplayName.MaxUtf8Length + SignatureLength;

    private const string TextMarker = "P2PM";
    private const char TextVersionDigit = '1';
    private const int MaxTextInputLength = 4096;
    private const int TextMarkerLength = 4;
    private const int MaxCompactTextLength = TextMarkerLength + 1 + (((MaxLength * 8) + 4) / 5);

    private readonly byte[] _bytes;

    private ContactCode(byte[] bytes, Ed25519PublicKey identityKey, X25519PublicKey staticKey, OnionAddress onionAddress, DisplayName displayName)
    {
        _bytes = bytes;
        IdentityKey = identityKey;
        StaticKey = staticKey;
        OnionAddress = onionAddress;
        DisplayName = displayName;
        Fingerprint = Fingerprint.Of(identityKey);
    }

    public Ed25519PublicKey IdentityKey { get; }

    public X25519PublicKey StaticKey { get; }

    public DisplayName DisplayName { get; }

    public OnionAddress OnionAddress { get; }

    public Fingerprint Fingerprint { get; }

    internal static ReadOnlySpan<byte> SignatureContext => "P2PM/contact-code/v1"u8;

    public static ContactCode Create(KeySet keySet, DisplayName displayName)
    {
        ArgumentNullException.ThrowIfNull(keySet);
        ArgumentNullException.ThrowIfNull(displayName);

        ReadOnlySpan<byte> name = displayName.Utf8Bytes;
        int bodyLength = HeaderLength + name.Length;
        byte[] bytes = new byte[bodyLength + SignatureLength];

        Span<byte> body = bytes.AsSpan(0, bodyLength);
        body[0] = Version;
        keySet.IdentityKey.Bytes.CopyTo(body[IdentityOffset..]);
        keySet.StaticKey.Bytes.CopyTo(body[StaticOffset..]);
        keySet.OnionAddress.Bytes.CopyTo(body[OnionOffset..]);
        body[NameLengthOffset] = (byte)name.Length;
        name.CopyTo(body[HeaderLength..]);

        keySet.IdentityKeyPair.Sign(SignedMessage(body), bytes.AsSpan(bodyLength));

        return new ContactCode(bytes, keySet.IdentityKey, keySet.StaticKey, keySet.OnionAddress, displayName);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out ContactCode? code, out ContactCodeError error)
    {
        code = null;
        error = ContactCodeError.Damaged;

        if (bytes.IsEmpty)
        {
            return false;
        }

        if (bytes[0] != Version)
        {
            error = ContactCodeError.UnsupportedVersion;
            return false;
        }

        if (bytes.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        int nameLength = bytes[NameLengthOffset];
        int bodyLength = HeaderLength + nameLength;
        if (nameLength is 0 or > DisplayName.MaxUtf8Length || bytes.Length != bodyLength + SignatureLength)
        {
            return false;
        }

        ReadOnlySpan<byte> body = bytes[..bodyLength];

        if (!Ed25519PublicKey.TryCreate(body.Slice(IdentityOffset, Ed25519PublicKey.Length), out Ed25519PublicKey? identityKey)
            || !X25519PublicKey.TryCreate(body.Slice(StaticOffset, X25519PublicKey.Length), out X25519PublicKey? staticKey)
            || !OnionAddress.TryCreate(body.Slice(OnionOffset, OnionAddress.Length), out OnionAddress? onionAddress)
            || !DisplayName.TryParse(body[HeaderLength..], out DisplayName? displayName)
            || !identityKey.Verify(SignedMessage(body), bytes[bodyLength..])
            || !staticKey.IsUsable())
        {
            return false;
        }

        code = new ContactCode(bytes.ToArray(), identityKey, staticKey, onionAddress, displayName);
        error = ContactCodeError.None;
        return true;
    }

    public static bool TryParseText(string? text, [NotNullWhen(true)] out ContactCode? code, out ContactCodeError error)
    {
        if (text is null)
        {
            code = null;
            error = ContactCodeError.NotAContactCode;
            return false;
        }

        return TryParseText(text.AsSpan(), out code, out error);
    }

    public static bool TryParseText(ReadOnlySpan<char> text, [NotNullWhen(true)] out ContactCode? code, out ContactCodeError error)
    {
        code = null;
        error = ContactCodeError.NotAContactCode;

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
                error = ContactCodeError.UnsupportedVersion;
            }

            return false;
        }

        Span<byte> bytes = stackalloc byte[MaxLength];
        if (!Base32.TryDecode(compact[(TextMarkerLength + 1)..], bytes, out int written))
        {
            error = ContactCodeError.Damaged;
            return false;
        }

        return TryParse(bytes[..written], out code, out error);
    }

    public byte[] ToBytes() => _bytes.ToArray();

    public string ToText() => TextPrefix + Base32.Encode(_bytes, lowerCase: false);

    public bool Equals(ContactCode? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is ContactCode other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    internal static byte[] SignedMessage(ReadOnlySpan<byte> body)
    {
        byte[] message = new byte[SignatureContext.Length + body.Length];
        SignatureContext.CopyTo(message);
        body.CopyTo(message.AsSpan(SignatureContext.Length));
        return message;
    }

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
