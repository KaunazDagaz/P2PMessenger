namespace P2PMessenger.Core.Messages;

public interface ISeenMessageIds
{
    ValueTask<bool> ContainsAsync(MessageId id, CancellationToken cancellationToken);

    ValueTask AddAsync(MessageId id, CancellationToken cancellationToken);
}
