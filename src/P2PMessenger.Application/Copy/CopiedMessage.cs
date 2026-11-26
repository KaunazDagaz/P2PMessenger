using System.Text;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Copy;

public sealed class CopiedMessage
{
    public CopiedMessage(int position, bool writtenByOwner, DisplayName? authorName, string text, DateTimeOffset writtenAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, CopyConversationStart.MaxMessages);
        ArgumentNullException.ThrowIfNull(text);
        if (writtenByOwner ? authorName is not null : authorName is null)
        {
            throw new ArgumentException("Only messages written by others carry an author name.", nameof(authorName));
        }

        if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > CopyConversationMessage.MaxUtf8Length)
        {
            throw new ArgumentException("Copied message text must be non-empty and within the maximum UTF-8 length.", nameof(text));
        }

        long milliseconds = writtenAt.ToUnixTimeMilliseconds();
        if (milliseconds is < 0 or > CopyConversationMessage.MaxWrittenAtMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(writtenAt), writtenAt, "Copied message timestamp is out of range.");
        }

        Position = position;
        WrittenByOwner = writtenByOwner;
        AuthorName = authorName;
        Text = text;
        WrittenAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    public int Position { get; }

    public bool WrittenByOwner { get; }

    public DisplayName? AuthorName { get; }

    public string Text { get; }

    public DateTimeOffset WrittenAt { get; }
}
