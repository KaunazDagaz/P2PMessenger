using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Sessions;

public static class SessionResponder
{
    public const int FirstFlightLength = SessionInitiator.FirstFlightLength;
    public const int ResponseLength = SessionInitiator.ResponseLength;

    public static bool TryAccept(
        KeySet own,
        SessionContact caller,
        ReadOnlySpan<byte> firstFlight,
        [NotNullWhen(true)] out SecureSession? session,
        [NotNullWhen(true)] out byte[]? response)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(caller);

        return TryAccept(own, caller, firstFlight, default, out session, out response);
    }

    internal static bool TryAccept(
        KeySet own,
        SessionContact caller,
        ReadOnlySpan<byte> firstFlight,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey,
        [NotNullWhen(true)] out SecureSession? session,
        [NotNullWhen(true)] out byte[]? response)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(caller);

        session = null;
        response = null;
        if (firstFlight.Length != FirstFlightLength)
        {
            return false;
        }

        ReadOnlySpan<byte> hint = firstFlight[..ConnectionHint.Length];
        ReadOnlySpan<byte> header = firstFlight.Slice(ConnectionHint.Length, Frame.HeaderLength);
        if (!ConnectionHint.Matches(caller, hint) || !Frame.TryReadHandshakeLength(header, out _))
        {
            return false;
        }

        byte[] prologue = SessionPrologue.Build(hint, caller.IdentityKey, own.IdentityKey);
        using KkHandshake handshake = new(NoiseHash.Blake2b, initiator: false, prologue, own.StaticKeyPair, caller.StaticKey, fixedEphemeralPrivateKey);
        if (!handshake.TryReadMessage(firstFlight[(ConnectionHint.Length + Frame.HeaderLength)..], Span<byte>.Empty, out int payloadLength)
            || payloadLength != 0)
        {
            return false;
        }

        byte[] reply = new byte[ResponseLength];
        if (!handshake.TryWriteMessage(default, reply.AsSpan(Frame.HeaderLength), out int written)
            || written != Frame.HandshakeMessageLength
            || !handshake.IsCompleted)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(reply, (ushort)written);
        session = new SecureSession(caller, isInitiator: false, handshake.TakeTransport(), handshake.HandshakeHash.ToArray());
        response = reply;
        return true;
    }
}
