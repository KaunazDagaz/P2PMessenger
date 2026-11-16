using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Transport.Connections;

internal readonly record struct ResponderOutcome(SecureSession? Session, SessionMessage? FirstMessage, HandshakeRefusal Refusal)
{
    public static ResponderOutcome Accepted(SecureSession session, SessionMessage firstMessage) => new(session, firstMessage, HandshakeRefusal.None);

    public static ResponderOutcome Refused(HandshakeRefusal refusal) => new(null, null, refusal);
}
