using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Copy;

public sealed class CopyConversationEnd : CopyMessage, IEquatable<CopyConversationEnd>
{
    private CopyConversationEnd(CopyOriginId origin)
    {
        Origin = origin;
    }

    public CopyOriginId Origin { get; }

    public override CopyMessageType Type => CopyMessageType.ConversationEnd;

    internal override int BodyLength => CopyOriginId.Length;

    public static CopyConversationEnd Create(CopyOriginId origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new CopyConversationEnd(origin);
    }

    public bool Equals(CopyConversationEnd? other) => other is not null && Origin.Equals(other.Origin);

    public override bool Equals(object? obj) => obj is CopyConversationEnd other && Equals(other);

    public override int GetHashCode() => Origin.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyConversationEnd? message)
    {
        if (!CopyOriginId.TryCreate(body, out CopyOriginId? origin))
        {
            message = null;
            return false;
        }

        message = new CopyConversationEnd(origin);
        return true;
    }

    internal override void WriteBody(Span<byte> body) => Origin.Bytes.CopyTo(body);
}
