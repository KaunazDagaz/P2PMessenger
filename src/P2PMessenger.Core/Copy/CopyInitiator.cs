using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Copy;

public sealed class CopyInitiator : IDisposable
{
    private readonly NkPsk0Handshake handshake;
    private readonly byte[] firstMessage;
    private bool finished;
    private bool disposed;

    private CopyInitiator(NkPsk0Handshake handshake, byte[] firstMessage)
    {
        this.handshake = handshake;
        this.firstMessage = firstMessage;
    }

    public ReadOnlySpan<byte> FirstMessage => firstMessage;

    public static CopyInitiator Start(CopyPairingCode pairingCode) => Start(pairingCode, default);

    internal static CopyInitiator Start(CopyPairingCode pairingCode, ReadOnlySpan<byte> fixedEphemeralPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(pairingCode);

        byte[] prologue = CopyPrologue.Build(pairingCode);
        NkPsk0Handshake handshake = NkPsk0Handshake.CreateInitiator(
            NoiseHash.Blake2b,
            prologue,
            pairingCode.PairingSecret,
            pairingCode.ReceiverPublicKey,
            fixedEphemeralPrivateKey);
        try
        {
            byte[] frame = new byte[CopyFrame.HandshakeFrameLength];
            BinaryPrimitives.WriteUInt16BigEndian(frame, CopyFrame.HandshakeMessageLength);
            if (!handshake.TryWriteMessage(default, frame.AsSpan(CopyFrame.HeaderLength), out int written) || written != CopyFrame.HandshakeMessageLength)
            {
                throw new InvalidOperationException("The pairing handshake message could not be written.");
            }

            return new CopyInitiator(handshake, frame);
        }
        catch
        {
            handshake.Dispose();
            throw;
        }
    }

    public bool TryFinish(ReadOnlySpan<byte> response, [NotNullWhen(true)] out CopySession? session)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        session = null;
        if (finished)
        {
            return false;
        }

        finished = true;
        if (response.Length != CopyFrame.HandshakeFrameLength
            || !CopyFrame.TryReadHandshakeLength(response[..CopyFrame.HeaderLength], out _)
            || !handshake.TryReadMessage(response[CopyFrame.HeaderLength..], Span<byte>.Empty, out int payloadLength)
            || payloadLength != 0)
        {
            handshake.Dispose();
            return false;
        }

        byte[] hash = handshake.HandshakeHash.ToArray();
        TransportCipher transport = handshake.TakeTransport();
        handshake.Dispose();
        session = new CopySession(true, transport, hash);
        return true;
    }

    public void Dispose()
    {
        disposed = true;
        handshake.Dispose();
    }
}
