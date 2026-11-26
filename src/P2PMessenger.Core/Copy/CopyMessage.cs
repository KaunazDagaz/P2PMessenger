namespace P2PMessenger.Core.Copy;

public abstract class CopyMessage
{
    internal CopyMessage()
    {
    }

    public abstract CopyMessageType Type { get; }

    internal abstract int BodyLength { get; }

    internal abstract void WriteBody(Span<byte> body);
}
