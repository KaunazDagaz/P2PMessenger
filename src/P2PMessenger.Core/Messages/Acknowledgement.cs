using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Messages;

public sealed class Acknowledgement : SessionMessage, IEquatable<Acknowledgement>
{
    private Acknowledgement(MessageId id)
    {
        Id = id;
    }

    public MessageId Id { get; }

    public override SessionMessageType Type => SessionMessageType.Acknowledgement;

    internal override int BodyLength => MessageId.Length;

    public static Acknowledgement Create(MessageId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Acknowledgement(id);
    }

    public bool Equals(Acknowledgement? other) => other is not null && Id.Equals(other.Id);

    public override bool Equals(object? obj) => obj is Acknowledgement other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out Acknowledgement? acknowledgement)
    {
        if (!MessageId.TryCreate(body, out MessageId? id))
        {
            acknowledgement = null;
            return false;
        }

        acknowledgement = new Acknowledgement(id);
        return true;
    }

    internal override void WriteBody(Span<byte> body) => Id.Bytes.CopyTo(body);
}
