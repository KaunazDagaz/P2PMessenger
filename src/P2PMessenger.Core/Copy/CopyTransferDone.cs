using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Copy;

public sealed class CopyTransferDone : CopyMessage, IEquatable<CopyTransferDone>
{
    private CopyTransferDone(int conversationCount)
    {
        ConversationCount = conversationCount;
    }

    public int ConversationCount { get; }

    public override CopyMessageType Type => CopyMessageType.TransferDone;

    internal override int BodyLength => sizeof(int);

    public static bool TryCreate(int conversationCount, [NotNullWhen(true)] out CopyTransferDone? message)
    {
        if (conversationCount is < 1 or > CopyTransferStart.MaxConversations)
        {
            message = null;
            return false;
        }

        message = new CopyTransferDone(conversationCount);
        return true;
    }

    public bool Equals(CopyTransferDone? other) => other is not null && ConversationCount == other.ConversationCount;

    public override bool Equals(object? obj) => obj is CopyTransferDone other && Equals(other);

    public override int GetHashCode() => ConversationCount.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyTransferDone? message)
    {
        if (body.Length != sizeof(int))
        {
            message = null;
            return false;
        }

        return TryCreate(BinaryPrimitives.ReadInt32BigEndian(body), out message);
    }

    internal override void WriteBody(Span<byte> body) => BinaryPrimitives.WriteInt32BigEndian(body, ConversationCount);
}
