using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Identity;

public sealed class FingerprintPair
{
    private FingerprintPair(Fingerprint own, Fingerprint contact, bool ownFirst)
    {
        Own = own;
        Contact = contact;
        OwnFirst = ownFirst;
    }

    public Fingerprint Own { get; }

    public Fingerprint Contact { get; }

    public bool OwnFirst { get; }

    public Fingerprint First => OwnFirst ? Own : Contact;

    public Fingerprint Second => OwnFirst ? Contact : Own;

    public static FingerprintPair Create(Ed25519PublicKey ownIdentityKey, Ed25519PublicKey contactIdentityKey)
    {
        ArgumentNullException.ThrowIfNull(ownIdentityKey);
        ArgumentNullException.ThrowIfNull(contactIdentityKey);

        bool ownFirst = ownIdentityKey.Bytes.SequenceCompareTo(contactIdentityKey.Bytes) <= 0;
        return new FingerprintPair(Fingerprint.Of(ownIdentityKey), Fingerprint.Of(contactIdentityKey), ownFirst);
    }
}
