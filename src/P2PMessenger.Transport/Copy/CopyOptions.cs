using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Copy;

public sealed record CopyOptions
{
    public const int DefaultMaxMessagesPerConversation = CopyConversationStart.MaxMessages;
    public const int DefaultMaxConversationBytes = 32 * 1024 * 1024;
    public const int MaxAllowedConversationBytes = 256 * 1024 * 1024;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaxMessagesPerConversation { get; init; } = DefaultMaxMessagesPerConversation;

    public int MaxConversationBytes { get; init; } = DefaultMaxConversationBytes;

    public AdmissionLimits Admission { get; init; } = new()
    {
        MaxPendingConnections = 8,
        MaxPendingPerSource = 4,
        HandshakesPerSecond = 10,
        HandshakeBurst = 16,
        HandshakesPerSecondPerSource = 4,
        HandshakeBurstPerSource = 8,
        MaxTrackedSources = 256,
    };

    public string? FindProblem()
    {
        if (!IsPositive(ConnectTimeout)
            || !IsPositive(HandshakeTimeout)
            || !IsPositive(KeepAliveInterval)
            || !IsPositive(IdleTimeout)
            || !IsPositive(WriteTimeout))
        {
            return "Every PC copy timeout must be greater than zero and at most one day.";
        }

        if (KeepAliveInterval >= IdleTimeout)
        {
            return "The PC copy keep-alive interval must be shorter than the idle timeout.";
        }

        if (MaxMessagesPerConversation is < 1 or > CopyConversationStart.MaxMessages)
        {
            return "The maximum number of messages per copied conversation must be between 1 and 100,000.";
        }

        if (MaxConversationBytes is < 1 or > MaxAllowedConversationBytes)
        {
            return "The maximum byte size of a copied conversation must be between 1 byte and 256 MiB.";
        }

        return Admission is null ? "Admission limits are required." : Admission.FindProblem();
    }

    private static bool IsPositive(TimeSpan value) => value > TimeSpan.Zero && value <= TimeSpan.FromDays(1);
}
