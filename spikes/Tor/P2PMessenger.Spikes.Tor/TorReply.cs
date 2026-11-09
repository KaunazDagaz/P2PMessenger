namespace P2PMessenger.Spikes.Tor;

public sealed record TorReplyLine(string Text, string? Data);

public sealed class TorReply
{
    public TorReply(int status, IReadOnlyList<TorReplyLine> lines)
    {
        Status = status;
        Lines = lines;
    }

    public int Status { get; }

    public IReadOnlyList<TorReplyLine> Lines { get; }

    public bool IsSuccess => Status is >= 250 and <= 251;

    public bool IsEvent => Status == 650;

    public string FirstLine => Lines.Count > 0 ? Lines[0].Text : string.Empty;
}
