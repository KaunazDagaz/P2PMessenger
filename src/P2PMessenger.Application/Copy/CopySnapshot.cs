using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Copy;

internal sealed record CopySnapshot(CopyOriginId Origin, DisplayName Title, IReadOnlyList<CopiedMessage> Messages);
