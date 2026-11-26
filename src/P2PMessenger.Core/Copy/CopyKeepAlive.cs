namespace P2PMessenger.Core.Copy;

public sealed class CopyKeepAlive : CopyMessage
{
    private CopyKeepAlive()
    {
    }

    public static CopyKeepAlive Instance { get; } = new();

    public override CopyMessageType Type => CopyMessageType.KeepAlive;

    internal override int BodyLength => 0;

    internal override void WriteBody(Span<byte> body)
    {
    }
}
