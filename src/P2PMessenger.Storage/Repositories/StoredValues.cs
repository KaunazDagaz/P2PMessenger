using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

internal static class StoredValues
{
    public const int TimeLength = sizeof(long);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Open(RecordKeys keys, RecordLocation location, byte[] sealedValue) =>
        keys.TryOpen(location, sealedValue, out byte[]? plaintext) ? plaintext : throw Damaged(location);

    public static StoredDataException Damaged(RecordLocation location) =>
        new(string.Create(CultureInfo.InvariantCulture, $"The stored value {location.Table}.{location.Column} of row {location.Row} is damaged or was changed."));

    public static StoredDataException Inconsistent(string what) =>
        new($"The stored data is inconsistent: {what}.");

    public static Ed25519PublicKey IdentityKey(RecordKeys keys, long personId, byte[] lookup, byte[] sealedKey)
    {
        RecordLocation location = Locations.PersonIdentityKey(personId);
        byte[] bytes = Open(keys, location, sealedKey);
        if (!Ed25519PublicKey.TryCreate(bytes, out Ed25519PublicKey? key) || !keys.MatchesLookup(LookupDomain.IdentityKey, bytes, lookup))
        {
            throw Damaged(location);
        }

        return key;
    }

    public static DisplayName Name(RecordKeys keys, long personId, byte[] sealedName)
    {
        RecordLocation location = Locations.PersonName(personId);
        string text = Text(location, Open(keys, location, sealedName));
        if (!DisplayName.TryCreate(text, out DisplayName? name) || !string.Equals(name.Value, text, StringComparison.Ordinal))
        {
            throw Damaged(location);
        }

        return name;
    }

    public static byte[] Utf8(string text) => StrictUtf8.GetBytes(text);

    public static string Text(RecordLocation location, byte[] utf8)
    {
        try
        {
            return StrictUtf8.GetString(utf8);
        }
        catch (DecoderFallbackException exception)
        {
            throw new StoredDataException(Damaged(location).Message, exception);
        }
    }

    public static byte[] Time(DateTimeOffset time)
    {
        byte[] bytes = new byte[TimeLength];
        BinaryPrimitives.WriteInt64BigEndian(bytes, time.UtcTicks);
        return bytes;
    }

    public static DateTimeOffset Time(RecordLocation location, byte[] bytes)
    {
        if (bytes.Length != TimeLength)
        {
            throw Damaged(location);
        }

        long ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            throw Damaged(location);
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
