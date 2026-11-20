using P2PMessenger.Application.Contacts;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

internal sealed class DatabaseContactStore : IContactStore
{
    private readonly ContactRepository repository;

    public DatabaseContactStore(ContactRepository repository)
    {
        this.repository = repository;
    }

    public async ValueTask<IReadOnlyList<Contact>> LoadAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredContact> stored = await repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. stored.Select(ToContact)];
    }

    public async ValueTask SaveAsync(Contact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        await repository.SaveAsync(ToStored(contact), cancellationToken).ConfigureAwait(false);
    }

    private static Contact ToContact(StoredContact stored) =>
        new(
            stored.Code,
            stored.Status == StoredContactStatus.Confirmed ? ContactState.Added : ContactState.WaitingForFirstConnection,
            stored.KeyChecked ? KeyComparison.CheckedByYou : KeyComparison.NotCompared);

    private static StoredContact ToStored(Contact contact) =>
        new(
            contact.Code,
            ContactChannel.OtherChannel,
            contact.State == ContactState.Added ? StoredContactStatus.Confirmed : StoredContactStatus.WaitingForFirstConnection,
            contact.KeyComparison == KeyComparison.CheckedByYou);
}
