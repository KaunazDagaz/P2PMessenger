using P2PMessenger.Core.Identity;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredCopiedMessage(
    int Position,
    bool WrittenByOwner,
    DisplayName? AuthorName,
    string Text,
    DateTimeOffset WrittenAt);
