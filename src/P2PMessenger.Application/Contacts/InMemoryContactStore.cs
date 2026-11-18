using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Contacts;

public sealed class InMemoryContactStore : IContactStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, Contact> contacts = [];

    public ValueTask<IReadOnlyList<Contact>> LoadAllAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<Contact>>([.. contacts.Values]);
        }
    }

    public ValueTask SaveAsync(Contact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        lock (gate)
        {
            contacts[contact.IdentityKey] = contact;
        }

        return ValueTask.CompletedTask;
    }
}
