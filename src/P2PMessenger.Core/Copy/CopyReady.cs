namespace P2PMessenger.Core.Copy;

public sealed class CopyReady : CopyMessage
{
    private CopyReady()
    {
    }

    public static CopyReady Instance { get; } = new();

    public override CopyMessageType Type => CopyMessageType.Ready;

    internal override int BodyLength => 0;

    internal override void WriteBody(Span<byte> body)
    {
    }
}
