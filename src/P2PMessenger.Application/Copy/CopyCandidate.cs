using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Copy;

public sealed record CopyCandidate(CopyOriginId Origin, DisplayName Title, bool IsGroup, int MessageCount, int TotalMessageCount)
{
    public bool IsTruncated => MessageCount < TotalMessageCount;
}
