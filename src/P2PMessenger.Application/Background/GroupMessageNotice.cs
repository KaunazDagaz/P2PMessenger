using P2PMessenger.Core.Groups;

namespace P2PMessenger.Application.Background;

public sealed record GroupMessageNotice(GroupId GroupId, string GroupName, string AuthorName, string? Preview);
