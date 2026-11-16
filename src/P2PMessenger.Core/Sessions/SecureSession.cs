using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Noise;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Sessions;

public sealed class SecureSession : IDisposable
{
    private readonly byte[] handshakeHash;
    private TransportCipher? transport;

    internal SecureSession(SessionContact peer, bool isInitiator, TransportCipher transport, byte[] handshakeHash)
    {
        Peer = peer;
        IsInitiator = isInitiator;
        IsConfirmed = isInitiator;
        this.transport = transport;
        this.handshakeHash = handshakeHash;
    }

    public SessionContact Peer { get; }

    public bool IsInitiator { get; }

    public bool IsConfirmed { get; private set; }

    public bool IsClosed => transport is null;

    internal ReadOnlySpan<byte> HandshakeHash => handshakeHash;

    public byte[] WriteFrame(SessionMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        TransportCipher? currentTransport = transport;
        ObjectDisposedException.ThrowIf(currentTransport is null, this);
        if (!IsConfirmed)
        {
            throw new InvalidOperationException("The responder writes nothing before the initiator's first transport message has been read.");
        }

        byte[] plaintext = MessageCodec.Encode(message);
        try
        {
            byte[] frame = new byte[Frame.HeaderLength + plaintext.Length + Frame.TagLength];
            int written = currentTransport.Encrypt(plaintext, frame.AsSpan(Frame.HeaderLength));
            BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)written);
            return frame;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public bool TryReadFrame(ReadOnlySpan<byte> body, [NotNullWhen(true)] out SessionMessage? message)
    {
        message = null;
        TransportCipher? currentTransport = transport;
        if (currentTransport is null)
        {
            return false;
        }

        if (!Frame.IsTransportBodyLength(body.Length))
        {
            Close();
            return false;
        }

        byte[] plaintext = new byte[body.Length - Frame.TagLength];
        try
        {
            if (!currentTransport.TryDecrypt(body, plaintext, out int length)
                || length != plaintext.Length
                || !MessageCodec.TryDecode(plaintext, out SessionMessage? decoded))
            {
                Close();
                return false;
            }

            IsConfirmed = true;
            message = decoded;
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose()
    {
        transport?.Dispose();
        transport = null;
        CryptographicOperations.ZeroMemory(handshakeHash);
    }

    private void Close()
    {
        transport?.Dispose();
        transport = null;
    }
}
