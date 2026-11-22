namespace P2PMessenger.Storage.Protection;

public sealed class WrappedDataKey
{
    public WrappedDataKey(DataKeyProtection protection, ReadOnlySpan<byte> wrappedKey)
        : this(protection, wrappedKey, null)
    {
    }

    public WrappedDataKey(DataKeyProtection protection, ReadOnlySpan<byte> wrappedKey, PassphraseDerivation? derivation)
    {
        if (!Enum.IsDefined(protection))
        {
            throw new ArgumentOutOfRangeException(nameof(protection), protection, "Unknown data-key protection.");
        }

        if (wrappedKey.IsEmpty)
        {
            throw new ArgumentException("The wrapped key must not be empty.", nameof(wrappedKey));
        }

        if ((protection == DataKeyProtection.Passphrase) != (derivation is not null))
        {
            throw new ArgumentException("A data key wrapped by a passphrase needs its key-derivation settings, and no other kind has any.", nameof(derivation));
        }

        Protection = protection;
        WrappedKey = wrappedKey.ToArray();
        Derivation = derivation;
    }

    public DataKeyProtection Protection { get; }

    public ReadOnlyMemory<byte> WrappedKey { get; }

    public PassphraseDerivation? Derivation { get; }
}
