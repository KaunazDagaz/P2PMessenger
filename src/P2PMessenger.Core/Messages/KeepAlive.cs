namespace P2PMessenger.Core.Messages;

public sealed class KeepAlive : SessionMessage
{
    private KeepAlive()
    {
    }

    public static KeepAlive Instance { get; } = new();

    public override SessionMessageType Type => SessionMessageType.KeepAlive;

    internal override int BodyLength => 0;

    internal override void WriteBody(Span<byte> body)
    {
    }
}
