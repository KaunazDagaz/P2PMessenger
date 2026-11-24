using P2PMessenger.Application.Background;

namespace P2PMessenger.Application.Tests.Background;

internal sealed class FakeNotifier : IMessageNotifier
{
    public bool IsAllowed { get; set; } = true;

    public List<MessageNotice> Shown { get; } = [];

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken) => Task.FromResult(IsAllowed);

    public void Show(MessageNotice notice)
    {
        lock (Shown)
        {
            Shown.Add(notice);
        }
    }
}
