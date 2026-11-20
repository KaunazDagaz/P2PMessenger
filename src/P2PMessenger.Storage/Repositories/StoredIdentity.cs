using P2PMessenger.Core.Identity;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredIdentity(KeySet Keys, DisplayName Name);
