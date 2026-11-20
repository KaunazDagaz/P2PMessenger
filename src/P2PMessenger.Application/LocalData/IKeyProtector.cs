namespace P2PMessenger.Application.LocalData;

public interface IKeyProtector
{
    KeyProtection Protection { get; }

    Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken);

    Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken);
}
