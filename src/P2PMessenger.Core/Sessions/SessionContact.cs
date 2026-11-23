using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Sessions;

public sealed class SessionContact : IDisposable
{
    private SessionContact(Ed25519PublicKey identityKey, X25519PublicKey staticKey, Key pairwiseKey, Key announcementKey, bool ownStaticKeyIsLower)
    {
        IdentityKey = identityKey;
        StaticKey = staticKey;
        PairwiseKey = pairwiseKey;
        AnnouncementKey = announcementKey;
        OwnStaticKeyIsLower = ownStaticKeyIsLower;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public X25519PublicKey StaticKey { get; }

    internal Key PairwiseKey { get; }

    internal Key AnnouncementKey { get; }

    internal bool OwnStaticKeyIsLower { get; }

    public static bool TryCreate(KeySet own, ContactCode code, [NotNullWhen(true)] out SessionContact? contact)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(code);

        return TryCreate(own, code.IdentityKey, code.StaticKey, out contact);
    }

    public static bool TryCreate(KeySet own, Ed25519PublicKey identityKey, X25519PublicKey staticKey, [NotNullWhen(true)] out SessionContact? contact)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(staticKey);

        contact = null;
        if (identityKey.Equals(own.IdentityKey)
            || staticKey.Equals(own.StaticKey)
            || !PairwiseKeys.TryDerive(own.StaticKeyPair, staticKey, out Key? pairwiseKey, out Key? announcementKey))
        {
            return false;
        }

        bool ownStaticKeyIsLower = own.StaticKey.Bytes.SequenceCompareTo(staticKey.Bytes) < 0;
        contact = new SessionContact(identityKey, staticKey, pairwiseKey, announcementKey, ownStaticKeyIsLower);
        return true;
    }

    public void Dispose()
    {
        PairwiseKey.Dispose();
        AnnouncementKey.Dispose();
    }
}
