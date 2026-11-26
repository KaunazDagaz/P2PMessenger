using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Copy;

public sealed class CopyConversationSaved : CopyMessage, IEquatable<CopyConversationSaved>
{
    private CopyConversationSaved(CopyOriginId origin)
    {
        Origin = origin;
    }

    public CopyOriginId Origin { get; }

    public override CopyMessageType Type => CopyMessageType.ConversationSaved;

    internal override int BodyLength => CopyOriginId.Length;

    public static CopyConversationSaved Create(CopyOriginId origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new CopyConversationSaved(origin);
    }

    public bool Equals(CopyConversationSaved? other) => other is not null && Origin.Equals(other.Origin);

    public override bool Equals(object? obj) => obj is CopyConversationSaved other && Equals(other);

    public override int GetHashCode() => Origin.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyConversationSaved? message)
    {
        if (!CopyOriginId.TryCreate(body, out CopyOriginId? origin))
        {
            message = null;
            return false;
        }

        message = new CopyConversationSaved(origin);
        return true;
    }

    internal override void WriteBody(Span<byte> body) => Origin.Bytes.CopyTo(body);
}
