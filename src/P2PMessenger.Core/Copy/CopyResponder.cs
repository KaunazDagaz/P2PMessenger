using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Copy;

public static class CopyResponder
{
    public static bool TryAccept(
        X25519KeyPair receiverKeyPair,
        CopyPairingCode pairingCode,
        ReadOnlySpan<byte> firstMessage,
        [NotNullWhen(true)] out CopySession? session,
        [NotNullWhen(true)] out byte[]? response) =>
        TryAccept(receiverKeyPair, pairingCode, firstMessage, default, out session, out response);

    internal static bool TryAccept(
        X25519KeyPair receiverKeyPair,
        CopyPairingCode pairingCode,
        ReadOnlySpan<byte> firstMessage,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey,
        [NotNullWhen(true)] out CopySession? session,
        [NotNullWhen(true)] out byte[]? response)
    {
        ArgumentNullException.ThrowIfNull(receiverKeyPair);
        ArgumentNullException.ThrowIfNull(pairingCode);

        session = null;
        response = null;
        if (!receiverKeyPair.PublicKey.Equals(pairingCode.ReceiverPublicKey))
        {
            throw new ArgumentException("The pairing code does not match the receiver key pair.", nameof(pairingCode));
        }

        if (firstMessage.Length != CopyFrame.HandshakeFrameLength
            || !CopyFrame.TryReadHandshakeLength(firstMessage[..CopyFrame.HeaderLength], out _))
        {
            return false;
        }

        byte[] prologue = CopyPrologue.Build(pairingCode);
        using NkPsk0Handshake handshake = NkPsk0Handshake.CreateResponder(
            NoiseHash.Blake2b,
            prologue,
            pairingCode.PairingSecret,
            receiverKeyPair,
            fixedEphemeralPrivateKey);

        if (!handshake.TryReadMessage(firstMessage[CopyFrame.HeaderLength..], Span<byte>.Empty, out int payloadLength) || payloadLength != 0)
        {
            return false;
        }

        byte[] reply = new byte[CopyFrame.HandshakeFrameLength];
        BinaryPrimitives.WriteUInt16BigEndian(reply, CopyFrame.HandshakeMessageLength);
        if (!handshake.TryWriteMessage(default, reply.AsSpan(CopyFrame.HeaderLength), out int written) || written != CopyFrame.HandshakeMessageLength)
        {
            return false;
        }

        byte[] hash = handshake.HandshakeHash.ToArray();
        TransportCipher transport = handshake.TakeTransport();
        session = new CopySession(false, transport, hash);
        response = reply;
        return true;
    }
}
