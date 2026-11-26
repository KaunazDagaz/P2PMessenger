using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Transport.Copy;

public sealed class CopyConnectResult
{
    private CopyConnectResult(CopyChannel? channel, CopyConnectFailure failure)
    {
        Channel = channel;
        Failure = failure;
    }

    public CopyChannel? Channel { get; }

    public CopyConnectFailure Failure { get; }

    [MemberNotNullWhen(true, nameof(Channel))]
    public bool IsConnected => Channel is not null;

    public static CopyConnectResult Connected(CopyChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new CopyConnectResult(channel, CopyConnectFailure.None);
    }

    public static CopyConnectResult Failed(CopyConnectFailure failure) => new(null, failure);
}
