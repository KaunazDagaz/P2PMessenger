using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Contacts;

public sealed class ContactBook : IDisposable
{
    private readonly OwnIdentity own;
    private readonly IContactStore store;
    private readonly SemaphoreSlim writing = new(1, 1);
    private Contact[] contacts;

    internal ContactBook(OwnIdentity own, IContactStore store, IEnumerable<Contact> loaded)
    {
        this.own = own;
        this.store = store;
        contacts = Ordered(loaded);
    }

    public event EventHandler<ContactChangedEventArgs>? Changed;

    public IReadOnlyList<Contact> All => Volatile.Read(ref contacts);

    public bool TryGet(Ed25519PublicKey identityKey, [NotNullWhen(true)] out Contact? contact)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        contact = Array.Find(Volatile.Read(ref contacts), known => known.IdentityKey.Equals(identityKey));
        return contact is not null;
    }

    public CodeCheck Check(string? text)
    {
        if (!ContactCode.TryParseText(text, out ContactCode? code, out ContactCodeError error))
        {
            return CodeCheck.Unreadable(error switch
            {
                ContactCodeError.UnsupportedVersion => CodeCheckOutcome.UnsupportedVersion,
                ContactCodeError.Damaged => CodeCheckOutcome.Damaged,
                _ => CodeCheckOutcome.NotACode,
            });
        }

        return Check(code);
    }

    public CodeCheck Check(ContactCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Classify(code, Volatile.Read(ref contacts));
    }

    public Task<AddContactResult> AddAsync(CodeCheck check, CancellationToken cancellationToken) =>
        AddAsync(check, CodeExchange.OtherChannel, cancellationToken);

    public async Task<AddContactResult> AddAsync(CodeCheck check, CodeExchange exchange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!check.CanAdd)
        {
            return new AddContactResult(null, AddContactOutcome.NotAddable);
        }

        ContactChangedEventArgs change;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Contact[] current = contacts;
            CodeCheck now = Classify(check.Code, current);
            if (now.Outcome != check.Outcome || !SameContact(now.Existing, check.Existing))
            {
                return new AddContactResult(null, AddContactOutcome.Stale);
            }

            if (now.Outcome == CodeCheckOutcome.NewConnectionKeys && now.Existing is { } existing)
            {
                Contact replaced = exchange == CodeExchange.InPerson
                    ? Contact.ScannedInPerson(check.Code)
                    : existing.WithNewConnectionKeys(check.Code);
                await store.SaveAsync(replaced, cancellationToken).ConfigureAwait(false);
                Replace(current, existing, replaced);
                change = new ContactChangedEventArgs(replaced, ContactChange.KeysReplaced, existing);
            }
            else
            {
                Contact added = exchange == CodeExchange.InPerson
                    ? Contact.ScannedInPerson(check.Code)
                    : new(check.Code, ContactState.WaitingForFirstConnection, KeyComparison.NotCompared, CodeExchange.OtherChannel);
                await store.SaveAsync(added, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref contacts, Ordered([.. current, added]));
                change = new ContactChangedEventArgs(added, ContactChange.Added, null);
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, change);
        return new AddContactResult(change.Contact, change.Change == ContactChange.KeysReplaced ? AddContactOutcome.KeysReplaced : AddContactOutcome.Added);
    }

    public Task<bool> SetKeyComparisonAsync(Ed25519PublicKey identityKey, KeyComparison keyComparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        return UpdateAsync(
            identityKey,
            contact => contact.KeyComparison == keyComparison ? null : contact.WithKeyComparison(keyComparison),
            ContactChange.KeyComparisonChanged,
            cancellationToken);
    }

    public async Task<bool> MarkScannedInPersonAsync(CodeCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (check.Outcome != CodeCheckOutcome.AlreadyContact || check.Code is not { } code || check.Existing is not { } existing)
        {
            return false;
        }

        ContactChangedEventArgs change;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Contact[] current = contacts;
            CodeCheck now = Classify(code, current);
            if (now.Outcome != CodeCheckOutcome.AlreadyContact || now.Existing is not { } known || !SameContact(known, existing))
            {
                return false;
            }

            if (known.KeyComparison == KeyComparison.CheckedByYou)
            {
                return true;
            }

            Contact updated = known.WithKeyComparison(KeyComparison.CheckedByYou);
            await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            Replace(current, known, updated);
            change = new ContactChangedEventArgs(updated, ContactChange.KeyComparisonChanged, known);
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, change);
        return true;
    }

    public void Dispose() => writing.Dispose();

    internal Task<bool> ConfirmFirstConnectionAsync(Ed25519PublicKey identityKey, X25519PublicKey staticKey, CancellationToken cancellationToken) =>
        UpdateAsync(
            identityKey,
            contact => contact.State == ContactState.WaitingForFirstConnection && contact.Code.StaticKey.Equals(staticKey)
                ? contact.WithState(ContactState.Added)
                : null,
            ContactChange.FirstConnection,
            cancellationToken);

    private static bool SameContact(Contact? first, Contact? second) =>
        first is null ? second is null : second is not null && first.IdentityKey.Equals(second.IdentityKey);

    private static Contact[] Ordered(IEnumerable<Contact> unordered) =>
        [.. unordered
            .OrderBy(static contact => contact.Name.Value, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static contact => Convert.ToHexString(contact.IdentityKey.Bytes), StringComparer.Ordinal)];

    private void Replace(Contact[] current, Contact previous, Contact replacement) =>
        Volatile.Write(ref contacts, Ordered(current.Select(contact => ReferenceEquals(contact, previous) ? replacement : contact)));

    private CodeCheck Classify(ContactCode code, Contact[] current)
    {
        if (code.IdentityKey.Equals(own.Keys.IdentityKey))
        {
            return CodeCheck.Of(CodeCheckOutcome.OwnCode, code, null);
        }

        if (code.StaticKey.Equals(own.Keys.StaticKey) || code.OnionAddress.Equals(own.Keys.OnionAddress))
        {
            return CodeCheck.Of(CodeCheckOutcome.KeysInUse, code, null);
        }

        Contact? sameIdentity = Array.Find(current, contact => contact.IdentityKey.Equals(code.IdentityKey));
        bool keysInUse = Array.Exists(
            current,
            contact => !contact.IdentityKey.Equals(code.IdentityKey)
                && (contact.Code.StaticKey.Equals(code.StaticKey) || contact.Code.OnionAddress.Equals(code.OnionAddress)));

        if (sameIdentity is not null)
        {
            if (sameIdentity.Code.StaticKey.Equals(code.StaticKey) && sameIdentity.Code.OnionAddress.Equals(code.OnionAddress))
            {
                return CodeCheck.Of(CodeCheckOutcome.AlreadyContact, code, sameIdentity);
            }

            return keysInUse
                ? CodeCheck.Of(CodeCheckOutcome.KeysInUse, code, null)
                : CodeCheck.Of(CodeCheckOutcome.NewConnectionKeys, code, sameIdentity);
        }

        if (keysInUse)
        {
            return CodeCheck.Of(CodeCheckOutcome.KeysInUse, code, null);
        }

        Contact? sameName = Array.Find(
            current,
            contact => string.Equals(contact.Name.Value, code.DisplayName.Value, StringComparison.OrdinalIgnoreCase));

        return sameName is not null
            ? CodeCheck.Of(CodeCheckOutcome.LooksLikeContact, code, sameName)
            : CodeCheck.Of(CodeCheckOutcome.New, code, null);
    }

    private async Task<bool> UpdateAsync(
        Ed25519PublicKey identityKey,
        Func<Contact, Contact?> update,
        ContactChange kind,
        CancellationToken cancellationToken)
    {
        ContactChangedEventArgs change;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Contact[] current = contacts;
            Contact? existing = Array.Find(current, contact => contact.IdentityKey.Equals(identityKey));
            Contact? updated = existing is null ? null : update(existing);
            if (existing is null || updated is null)
            {
                return false;
            }

            await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            Replace(current, existing, updated);
            change = new ContactChangedEventArgs(updated, kind, existing);
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, change);
        return true;
    }
}
