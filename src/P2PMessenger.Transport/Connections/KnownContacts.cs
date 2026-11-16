using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Transport.Connections;

public sealed class KnownContacts : IKnownContacts
{
    private readonly Lock gate = new();
    private SessionContact[] current = [];

    public IReadOnlyList<SessionContact> Current => Volatile.Read(ref current);

    public bool TryAdd(SessionContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        lock (gate)
        {
            SessionContact[] snapshot = current;
            if (Array.Exists(snapshot, known => known.IdentityKey.Equals(contact.IdentityKey) || known.StaticKey.Equals(contact.StaticKey)))
            {
                return false;
            }

            SessionContact[] next = [.. snapshot, contact];
            Volatile.Write(ref current, next);
            return true;
        }
    }

    public bool TryRemove(Ed25519PublicKey identityKey, [NotNullWhen(true)] out SessionContact? removed)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        lock (gate)
        {
            SessionContact[] snapshot = current;
            int index = Array.FindIndex(snapshot, known => known.IdentityKey.Equals(identityKey));
            if (index < 0)
            {
                removed = null;
                return false;
            }

            removed = snapshot[index];
            SessionContact[] next = [.. snapshot[..index], .. snapshot[(index + 1)..]];
            Volatile.Write(ref current, next);
            return true;
        }
    }
}
