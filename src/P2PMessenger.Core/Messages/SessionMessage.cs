namespace P2PMessenger.Core.Messages;

public abstract class SessionMessage
{
    private protected SessionMessage()
    {
    }

    public abstract SessionMessageType Type { get; }

    internal abstract int BodyLength { get; }

    internal abstract void WriteBody(Span<byte> body);
}
