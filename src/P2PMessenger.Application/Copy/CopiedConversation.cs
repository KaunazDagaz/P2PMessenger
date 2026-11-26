using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Copy;

public sealed class CopiedConversation
{
    public CopiedConversation(CopyOriginId origin, DisplayName title, DateTimeOffset copiedAt, IReadOnlyList<CopiedMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count > CopyConversationStart.MaxMessages)
        {
            throw new ArgumentException("The copied conversation exceeds the maximum number of messages.", nameof(messages));
        }

        CopiedMessage[] copy = new CopiedMessage[messages.Count];
        for (int index = 0; index < messages.Count; index++)
        {
            CopiedMessage message = messages[index] ?? throw new ArgumentException("Copied messages cannot contain null entries.", nameof(messages));
            if (message.Position != index + 1)
            {
                throw new ArgumentException("Copied messages must have contiguous positions starting at 1.", nameof(messages));
            }

            copy[index] = message;
        }

        Origin = origin;
        Title = title;
        CopiedAt = copiedAt;
        Messages = copy;
    }

    public CopyOriginId Origin { get; }

    public DisplayName Title { get; }

    public DateTimeOffset CopiedAt { get; }

    public IReadOnlyList<CopiedMessage> Messages { get; }
}
