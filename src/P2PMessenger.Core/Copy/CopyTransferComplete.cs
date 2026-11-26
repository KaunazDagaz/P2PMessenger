using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Copy;

public sealed class CopyTransferComplete : CopyMessage, IEquatable<CopyTransferComplete>
{
    private CopyTransferComplete(int savedCount)
    {
        SavedCount = savedCount;
    }

    public int SavedCount { get; }

    public override CopyMessageType Type => CopyMessageType.TransferComplete;

    internal override int BodyLength => sizeof(int);

    public static bool TryCreate(int savedCount, [NotNullWhen(true)] out CopyTransferComplete? message)
    {
        if (savedCount is < 1 or > CopyTransferStart.MaxConversations)
        {
            message = null;
            return false;
        }

        message = new CopyTransferComplete(savedCount);
        return true;
    }

    public bool Equals(CopyTransferComplete? other) => other is not null && SavedCount == other.SavedCount;

    public override bool Equals(object? obj) => obj is CopyTransferComplete other && Equals(other);

    public override int GetHashCode() => SavedCount.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyTransferComplete? message)
    {
        if (body.Length != sizeof(int))
        {
            message = null;
            return false;
        }

        return TryCreate(BinaryPrimitives.ReadInt32BigEndian(body), out message);
    }

    internal override void WriteBody(Span<byte> body) => BinaryPrimitives.WriteInt32BigEndian(body, SavedCount);
}
