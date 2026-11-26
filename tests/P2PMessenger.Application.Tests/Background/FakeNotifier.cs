using P2PMessenger.Application.Background;

namespace P2PMessenger.Application.Tests.Background;

internal sealed class FakeNotifier : IMessageNotifier
{
    public bool IsAllowed { get; set; } = true;

    public List<MessageNotice> Shown { get; } = [];

    public List<GroupMessageNotice> ShownGroups { get; } = [];

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken) => Task.FromResult(IsAllowed);

    public void Show(MessageNotice notice)
    {
        lock (Shown)
        {
            Shown.Add(notice);
        }
    }

    public void ShowGroup(GroupMessageNotice notice)
    {
        lock (ShownGroups)
        {
            ShownGroups.Add(notice);
        }
    }
}
