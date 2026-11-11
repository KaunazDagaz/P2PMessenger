namespace P2PMessenger.Spikes.Nearby.Identification;

public sealed class ContactBook : IDisposable
{
    private readonly List<Contact> _contacts = [];

    private ContactBook(TestIdentity self)
    {
        Self = self;
    }

    public TestIdentity Self { get; }

    public IReadOnlyList<Contact> Contacts => _contacts;

    public static ContactBook Create(TestIdentity self, IEnumerable<TestIdentity> contacts, bool cacheStaticDh)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(contacts);
        ContactBook book = new(self);
        try
        {
            foreach (TestIdentity contact in contacts)
            {
                book.Add(contact.Name, contact.PublicKey.ToArray(), cacheStaticDh);
            }

            return book;
        }
        catch
        {
            book.Dispose();
            throw;
        }
    }

    public Contact Add(string name, byte[] publicKey, bool cacheStaticDh)
    {
        Contact contact = new(
            name,
            publicKey,
            PairwiseKeys.DeriveMacKey(Self.StaticKey, Self.PublicKey, publicKey),
            cacheStaticDh ? PairwiseKeys.ExportStaticDh(Self.StaticKey, publicKey) : null);
        _contacts.Add(contact);
        return contact;
    }

    public Contact? Find(string name) => _contacts.FirstOrDefault(contact => string.Equals(contact.Name, name, StringComparison.Ordinal));

    public void Dispose()
    {
        foreach (Contact contact in _contacts)
        {
            contact.Dispose();
        }

        _contacts.Clear();
    }
}
