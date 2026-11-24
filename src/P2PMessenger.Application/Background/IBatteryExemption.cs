namespace P2PMessenger.Application.Background;

public interface IBatteryExemption
{
    bool IsExempt { get; }

    bool IsRestricted { get; }

    Task<bool> RequestAsync(CancellationToken cancellationToken);

    Task<bool> OpenSettingsAsync(CancellationToken cancellationToken);
}
