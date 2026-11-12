using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace P2PMessenger.Core.Identity;

public sealed class DisplayName : IEquatable<DisplayName>
{
    public const int MaxUtf8Length = 64;

    private readonly byte[] _utf8;

    private DisplayName(string value, byte[] utf8)
    {
        Value = value;
        _utf8 = utf8;
    }

    public string Value { get; }

    internal ReadOnlySpan<byte> Utf8Bytes => _utf8;

    public static bool TryCreate(string? text, [NotNullWhen(true)] out DisplayName? name)
    {
        if (text is null)
        {
            name = null;
            return false;
        }

        return TryBuild(text.Trim(), out name);
    }

    public bool Equals(DisplayName? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DisplayName other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    internal static bool TryParse(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out DisplayName? name)
    {
        if (utf8.Length is 0 or > MaxUtf8Length || !Utf8.IsValid(utf8))
        {
            name = null;
            return false;
        }

        string text = Encoding.UTF8.GetString(utf8);
        if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            name = null;
            return false;
        }

        return TryBuild(text, out name);
    }

    private static bool TryBuild(string value, [NotNullWhen(true)] out DisplayName? name)
    {
        name = null;

        if (value.Length is 0 or > MaxUtf8Length || !IsAllowed(value))
        {
            return false;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length > MaxUtf8Length)
        {
            return false;
        }

        name = new DisplayName(value, utf8);
        return true;
    }

    private static bool IsAllowed(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune == Rune.ReplacementChar)
            {
                return false;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.Surrogate:
                    return false;
                case UnicodeCategory.Format when rune.Value is not (0x200C or 0x200D):
                    return false;
            }
        }

        return true;
    }
}
