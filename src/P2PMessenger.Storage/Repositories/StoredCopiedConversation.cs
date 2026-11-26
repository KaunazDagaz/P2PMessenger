using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredCopiedConversation(
    CopyOriginId Origin,
    DisplayName Title,
    DateTimeOffset CopiedAt,
    IReadOnlyList<StoredCopiedMessage> Messages);
