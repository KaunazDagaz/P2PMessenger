namespace P2PMessenger.Transport.Tor;

internal sealed class ControlReply
{
    public const int EventStatus = 650;

    public ControlReply(int status, IReadOnlyList<ControlReplyLine> lines)
    {
        Status = status;
        Lines = lines;
    }

    public int Status { get; }

    public IReadOnlyList<ControlReplyLine> Lines { get; }

    public bool IsSuccess => Status is 250 or 251;

    public bool IsEvent => Status == EventStatus;

    public string FirstLine => Lines.Count > 0 ? Lines[0].Text : string.Empty;
}
