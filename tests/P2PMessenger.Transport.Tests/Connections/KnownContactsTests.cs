using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Tests.Connections;

public sealed class KnownContactsTests
{
    [Fact]
    [Trait("Story", "US-05")]
    public void TryAdd_NewContact_IsInTheList()
    {
        using var own = KeySet.Create();
        using var friend = KeySet.Create();
        using var contact = Contact(own, friend);
        var contacts = new KnownContacts();

        Assert.True(contacts.TryAdd(contact));

        Assert.Same(contact, Assert.Single(contacts.Current));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void TryAdd_SameKeysTwice_IsRefused()
    {
        using var own = KeySet.Create();
        using var friend = KeySet.Create();
        using var contact = Contact(own, friend);
        using var duplicate = Contact(own, friend);
        var contacts = new KnownContacts();
        Assert.True(contacts.TryAdd(contact));

        Assert.False(contacts.TryAdd(duplicate));

        Assert.Single(contacts.Current);
    }

    [Fact]
    public void TryRemove_KnownContact_RemovesItAndKeepsTheOthers()
    {
        using var own = KeySet.Create();
        using var first = KeySet.Create();
        using var second = KeySet.Create();
        using var firstContact = Contact(own, first);
        using var secondContact = Contact(own, second);
        var contacts = new KnownContacts();
        Assert.True(contacts.TryAdd(firstContact));
        Assert.True(contacts.TryAdd(secondContact));

        Assert.True(contacts.TryRemove(first.IdentityKey, out var removed));

        Assert.Same(firstContact, removed);
        Assert.Same(secondContact, Assert.Single(contacts.Current));
        Assert.False(contacts.TryRemove(first.IdentityKey, out _));
    }

    [Fact]
    public void Current_TakenBeforeAChange_StaysAsItWas()
    {
        using var own = KeySet.Create();
        using var friend = KeySet.Create();
        using var contact = Contact(own, friend);
        var contacts = new KnownContacts();
        var before = contacts.Current;

        Assert.True(contacts.TryAdd(contact));

        Assert.Empty(before);
        Assert.Single(contacts.Current);
    }

    private static SessionContact Contact(KeySet own, KeySet other)
    {
        Assert.True(SessionContact.TryCreate(own, other.IdentityKey, other.StaticKey, out var contact));
        return contact;
    }
}
