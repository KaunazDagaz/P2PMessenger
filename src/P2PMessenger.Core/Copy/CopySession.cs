using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Noise;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Copy;

public sealed class CopySession : IDisposable
{
    private readonly byte[] handshakeHash;
    private TransportCipher? transport;

    internal CopySession(bool isInitiator, TransportCipher transport, byte[] handshakeHash)
    {
        IsInitiator = isInitiator;
        IsConfirmed = isInitiator;
        this.transport = transport;
        this.handshakeHash = handshakeHash;
    }

    public bool IsInitiator { get; }

    public bool IsConfirmed { get; private set; }

    public bool IsClosed => transport is null;

    internal ReadOnlySpan<byte> HandshakeHash => handshakeHash;

    public byte[] WriteFrame(CopyMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        TransportCipher? currentTransport = transport;
        ObjectDisposedException.ThrowIf(currentTransport is null, this);
        if (!IsConfirmed)
        {
            throw new InvalidOperationException("The responder writes nothing before the initiator's first transport message has been read.");
        }

        byte[] plaintext = CopyMessageCodec.Encode(message);
        try
        {
            byte[] frame = new byte[CopyFrame.HeaderLength + plaintext.Length + CopyFrame.TagLength];
            int written = currentTransport.Encrypt(plaintext, frame.AsSpan(CopyFrame.HeaderLength));
            BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)written);
            return frame;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public bool TryReadFrame(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CopyMessage? message)
    {
        message = null;
        TransportCipher? currentTransport = transport;
        if (currentTransport is null)
        {
            return false;
        }

        if (!CopyFrame.IsTransportBodyLength(body.Length))
        {
            Close();
            return false;
        }

        byte[] plaintext = new byte[body.Length - CopyFrame.TagLength];
        try
        {
            if (!currentTransport.TryDecrypt(body, plaintext, out int length)
                || length != plaintext.Length
                || !CopyMessageCodec.TryDecode(plaintext, out CopyMessage? decoded))
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
