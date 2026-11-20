namespace P2PMessenger.Storage.Protection;

public sealed class WrappedDataKey
{
    public WrappedDataKey(DataKeyProtection protection, ReadOnlySpan<byte> wrappedKey)
    {
        if (!Enum.IsDefined(protection))
        {
            throw new ArgumentOutOfRangeException(nameof(protection), protection, "Unknown data-key protection.");
        }

        if (wrappedKey.IsEmpty)
        {
            throw new ArgumentException("The wrapped key must not be empty.", nameof(wrappedKey));
        }

        Protection = protection;
        WrappedKey = wrappedKey.ToArray();
    }

    public DataKeyProtection Protection { get; }

    public ReadOnlyMemory<byte> WrappedKey { get; }
}
