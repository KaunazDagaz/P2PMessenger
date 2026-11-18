namespace P2PMessenger.Application.Contacts;

public interface IContactStore
{
    ValueTask<IReadOnlyList<Contact>> LoadAllAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(Contact contact, CancellationToken cancellationToken);
}
