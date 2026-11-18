using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Contacts;

public sealed class ContactBookTests : IDisposable
{
    private readonly OwnIdentity own = NewIdentity("Alice");
    private readonly KeySet bob = KeySet.Create();
    private readonly InMemoryContactStore store = new();
    private readonly ContactBook book;
    private readonly List<ContactChangedEventArgs> changes = [];

    public ContactBookTests()
    {
        book = new ContactBook(own, store, []);
        book.Changed += (_, change) => changes.Add(change);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(null, CodeCheckOutcome.NotACode)]
    [InlineData("", CodeCheckOutcome.NotACode)]
    [InlineData("hello", CodeCheckOutcome.NotACode)]
    [InlineData("P2PM2-AAAA", CodeCheckOutcome.UnsupportedVersion)]
    [InlineData("P2PM1-AEAAAAAA", CodeCheckOutcome.Damaged)]
    public void Check_UnreadableText_IsRefusedWithItsReason(string? text, CodeCheckOutcome expected)
    {
        var check = book.Check(text);

        Assert.Equal(expected, check.Outcome);
        Assert.False(check.CanAdd);
        Assert.Null(check.Code);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Check_CodeWithOneCharacterChanged_IsDamaged()
    {
        var text = Code(bob, "Bob").ToText().ToCharArray();
        text[40] = text[40] == 'A' ? 'B' : 'A';

        var check = book.Check(new string(text));

        Assert.Equal(CodeCheckOutcome.Damaged, check.Outcome);
        Assert.Empty(book.All);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Check_OwnCode_CannotBeAdded()
    {
        var check = book.Check(own.CodeText);

        Assert.Equal(CodeCheckOutcome.OwnCode, check.Outcome);
        Assert.False(check.CanAdd);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Check_CodeWithOwnStaticKeyUnderAnotherIdentity_CannotBeAdded()
    {
        using var copy = WithNewIdentityKey(own.Keys);

        var check = book.Check(Code(copy, "Mallory"));

        Assert.Equal(CodeCheckOutcome.KeysInUse, check.Outcome);
        Assert.False(check.CanAdd);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task AddAsync_NewCode_SavesAContactThatWaitsForTheFirstConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = book.Check(Code(bob, "Bob").ToText());

        var result = await book.AddAsync(check, cancellationToken);

        Assert.Equal(CodeCheckOutcome.New, check.Outcome);
        Assert.Equal(AddContactOutcome.Added, result.Outcome);
        Assert.True(result.IsSaved);
        var contact = result.Contact;
        Assert.Equal(ContactState.WaitingForFirstConnection, contact.State);
        Assert.False(contact.IsAdded);
        Assert.Equal(KeyComparison.NotCompared, contact.KeyComparison);
        Assert.Equal(bob.IdentityKey, contact.IdentityKey);
        Assert.Equal("Bob", contact.Name.Value);
        Assert.Equal(Fingerprint.Of(bob.IdentityKey), contact.Fingerprint);
        Assert.Same(contact, Assert.Single(book.All));
        Assert.True(book.TryGet(bob.IdentityKey, out var found));
        Assert.Same(contact, found);
        Assert.Same(contact, Assert.Single(await store.LoadAllAsync(cancellationToken)));
        var change = Assert.Single(changes);
        Assert.Equal(ContactChange.Added, change.Change);
        Assert.Same(contact, change.Contact);
        Assert.Null(change.Previous);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task Check_SameCodeAgain_IsAlreadyAContact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var added = await AddAsync(Code(bob, "Bob"), cancellationToken);

        var check = book.Check(Code(bob, "Bobby"));

        Assert.Equal(CodeCheckOutcome.AlreadyContact, check.Outcome);
        Assert.Same(added, check.Existing);
        Assert.False(check.CanAdd);
        Assert.Equal(AddContactOutcome.NotAddable, (await book.AddAsync(check, cancellationToken)).Outcome);
    }

    [Theory]
    [Trait("Story", "US-05")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddAsync_SameIdentityWithNewConnectionKeys_ReplacesTheKeysAndWaitsForTheFirstConnectionAgain(bool newOnionKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = await AddAsync(Code(bob, "Bob"), cancellationToken);
        Assert.True(await book.ConfirmFirstConnectionAsync(bob.IdentityKey, bob.StaticKey, cancellationToken));
        Assert.True(await book.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.CheckedByYou, cancellationToken));
        using var renewed = newOnionKey ? WithNewOnionKey(bob) : WithNewStaticKey(bob);
        changes.Clear();

        var check = book.Check(Code(renewed, "Bob"));
        var result = await book.AddAsync(check, cancellationToken);

        Assert.Equal(CodeCheckOutcome.NewConnectionKeys, check.Outcome);
        Assert.Equal(old.IdentityKey, check.Existing?.IdentityKey);
        Assert.Equal(AddContactOutcome.KeysReplaced, result.Outcome);
        Assert.True(result.IsSaved);
        Assert.Equal(renewed.StaticKey, result.Contact.Code.StaticKey);
        Assert.Equal(renewed.OnionAddress, result.Contact.Code.OnionAddress);
        Assert.Equal(ContactState.WaitingForFirstConnection, result.Contact.State);
        Assert.Equal(KeyComparison.CheckedByYou, result.Contact.KeyComparison);
        Assert.Same(result.Contact, Assert.Single(book.All));
        var change = Assert.Single(changes);
        Assert.Equal(ContactChange.KeysReplaced, change.Change);
        Assert.Equal(bob.StaticKey, change.Previous?.Code.StaticKey);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task AddAsync_AnotherIdentityUnderASavedName_IsAddedAsASecondContact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = await AddAsync(Code(bob, "Bob"), cancellationToken);
        using var reinstalled = KeySet.Create();

        var check = book.Check(Code(reinstalled, "BOB"));
        var result = await book.AddAsync(check, cancellationToken);

        Assert.Equal(CodeCheckOutcome.LooksLikeContact, check.Outcome);
        Assert.Same(old, check.Existing);
        Assert.Equal(AddContactOutcome.Added, result.Outcome);
        Assert.Equal(2, book.All.Count);
        Assert.Contains(book.All, contact => contact.IdentityKey.Equals(reinstalled.IdentityKey) && !contact.IsAdded);
        Assert.Contains(book.All, contact => ReferenceEquals(contact, old));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task Check_AnotherIdentityWithTheKeysOfASavedContact_CannotBeAdded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        using var copy = WithNewIdentityKey(bob);

        var check = book.Check(Code(copy, "Mallory"));

        Assert.Equal(CodeCheckOutcome.KeysInUse, check.Outcome);
        Assert.False(check.CanAdd);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task Check_SameIdentityWithTheStaticKeyOfAnotherContact_CannotBeAdded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var carol = KeySet.Create();
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        await AddAsync(Code(carol, "Carol"), cancellationToken);
        using var bobWithCarolsKeys = WithNewIdentityKey(carol);

        var check = book.Check(Code(bobWithCarolsKeys, "Bob"));

        Assert.Equal(CodeCheckOutcome.KeysInUse, check.Outcome);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task AddAsync_CheckOutdatedByAnotherAdd_IsStaleAndAddsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = Code(bob, "Bob");
        var first = book.Check(code);
        var second = book.Check(code);
        await book.AddAsync(first, cancellationToken);

        var result = await book.AddAsync(second, cancellationToken);

        Assert.Equal(AddContactOutcome.Stale, result.Outcome);
        Assert.False(result.IsSaved);
        Assert.Single(book.All);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task ConfirmFirstConnectionAsync_WaitingContactWithItsStaticKey_IsAddedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        changes.Clear();

        var first = await book.ConfirmFirstConnectionAsync(bob.IdentityKey, bob.StaticKey, cancellationToken);
        var second = await book.ConfirmFirstConnectionAsync(bob.IdentityKey, bob.StaticKey, cancellationToken);

        Assert.True(first);
        Assert.False(second);
        Assert.True(book.TryGet(bob.IdentityKey, out var contact));
        Assert.True(contact.IsAdded);
        var change = Assert.Single(changes);
        Assert.Equal(ContactChange.FirstConnection, change.Change);
        Assert.False(change.Previous?.IsAdded);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task ConfirmFirstConnectionAsync_AnotherStaticKeyOrUnknownContact_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stranger = KeySet.Create();
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        changes.Clear();

        Assert.False(await book.ConfirmFirstConnectionAsync(bob.IdentityKey, stranger.StaticKey, cancellationToken));
        Assert.False(await book.ConfirmFirstConnectionAsync(stranger.IdentityKey, stranger.StaticKey, cancellationToken));

        Assert.True(book.TryGet(bob.IdentityKey, out var contact));
        Assert.False(contact.IsAdded);
        Assert.Empty(changes);
    }

    [Fact]
    [Trait("Story", "US-04")]
    public async Task SetKeyComparisonAsync_CheckedAndBack_RaisesOneChangeEach()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        changes.Clear();

        Assert.True(await book.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.CheckedByYou, cancellationToken));
        Assert.False(await book.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.CheckedByYou, cancellationToken));
        Assert.True(book.TryGet(bob.IdentityKey, out var checkedContact));
        Assert.Equal(KeyComparison.CheckedByYou, checkedContact.KeyComparison);
        Assert.True(await book.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.NotCompared, cancellationToken));

        Assert.Equal([ContactChange.KeyComparisonChanged, ContactChange.KeyComparisonChanged], changes.Select(static change => change.Change));
        Assert.True(book.TryGet(bob.IdentityKey, out var contact));
        Assert.Equal(KeyComparison.NotCompared, contact.KeyComparison);
    }

    [Fact]
    [Trait("Story", "US-04")]
    public async Task SetKeyComparisonAsync_UnknownContact_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await book.SetKeyComparisonAsync(bob.IdentityKey, KeyComparison.CheckedByYou, cancellationToken));
        Assert.Empty(changes);
    }

    [Fact]
    public async Task All_SeveralContacts_AreOrderedByName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var carol = KeySet.Create();
        using var anna = KeySet.Create();
        await AddAsync(Code(carol, "carol"), cancellationToken);
        await AddAsync(Code(bob, "Bob"), cancellationToken);
        await AddAsync(Code(anna, "Anna"), cancellationToken);

        Assert.Equal(["Anna", "Bob", "carol"], book.All.Select(static contact => contact.Name.Value));
    }

    [Fact]
    public void Constructor_LoadedContacts_AreKnown()
    {
        var saved = new Contact(Code(bob, "Bob"), ContactState.Added, KeyComparison.CheckedByYou);

        using var loaded = new ContactBook(own, store, [saved]);

        Assert.True(loaded.TryGet(bob.IdentityKey, out var contact));
        Assert.Same(saved, contact);
        Assert.Equal(CodeCheckOutcome.AlreadyContact, loaded.Check(Code(bob, "Bob")).Outcome);
    }

    public void Dispose()
    {
        book.Dispose();
        bob.Dispose();
        own.Dispose();
    }

    private async Task<Contact> AddAsync(ContactCode code, CancellationToken cancellationToken)
    {
        var result = await book.AddAsync(book.Check(code), cancellationToken);
        Assert.True(result.IsSaved);
        return result.Contact;
    }
}
