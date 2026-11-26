using System.Buffers.Binary;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Copy;

public static class CopyFrame
{
    public const int HeaderLength = 2;
    public const int TagLength = CipherState.TagLength;
    public const int HandshakeMessageLength = NkPsk0Handshake.MessageOverhead;
    public const int HandshakeFrameLength = HeaderLength + HandshakeMessageLength;
    public const int MinTransportBodyLength = CopyMessageCodec.MinPaddedLength + TagLength;
    public const int MaxTransportBodyLength = CopyMessageCodec.MaxPaddedLength + TagLength;

    public static bool TryReadHandshakeLength(ReadOnlySpan<byte> header, out int length)
    {
        length = 0;
        if (header.Length != HeaderLength || BinaryPrimitives.ReadUInt16BigEndian(header) != HandshakeMessageLength)
        {
            return false;
        }

        length = HandshakeMessageLength;
        return true;
    }

    public static bool TryReadTransportLength(ReadOnlySpan<byte> header, out int length)
    {
        length = 0;
        if (header.Length != HeaderLength)
        {
            return false;
        }

        int value = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (!IsTransportBodyLength(value))
        {
            return false;
        }

        length = value;
        return true;
    }

    public static bool IsTransportBodyLength(int length) => CopyMessageCodec.IsPaddedLength(length - TagLength);
}
