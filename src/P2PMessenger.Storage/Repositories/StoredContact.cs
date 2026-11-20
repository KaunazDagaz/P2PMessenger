using P2PMessenger.Core.Contacts;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredContact(ContactCode Code, ContactChannel AddedVia, StoredContactStatus Status, bool KeyChecked);
