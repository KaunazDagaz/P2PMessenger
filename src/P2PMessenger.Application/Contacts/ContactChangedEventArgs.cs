namespace P2PMessenger.Application.Contacts;

public sealed class ContactChangedEventArgs : EventArgs
{
    public ContactChangedEventArgs(Contact contact, ContactChange change, Contact? previous)
    {
        ArgumentNullException.ThrowIfNull(contact);

        Contact = contact;
        Change = change;
        Previous = previous;
    }

    public Contact Contact { get; }

    public ContactChange Change { get; }

    public Contact? Previous { get; }
}
