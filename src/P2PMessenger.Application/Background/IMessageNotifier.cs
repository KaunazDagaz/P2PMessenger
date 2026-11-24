namespace P2PMessenger.Application.Background;

public interface IMessageNotifier
{
    bool IsAllowed { get; }

    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken);

    void Show(MessageNotice notice);
}
