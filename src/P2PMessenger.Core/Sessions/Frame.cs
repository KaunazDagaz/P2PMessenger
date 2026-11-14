using System.Buffers.Binary;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Sessions;

public static class Frame
{
    public const int HeaderLength = 2;
    public const int TagLength = 16;
    public const int HandshakeMessageLength = 48;
    public const int MinTransportBodyLength = MessageCodec.MinPaddedLength + TagLength;
    public const int MaxTransportBodyLength = MessageCodec.MaxPaddedLength + TagLength;

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

    public static bool IsTransportBodyLength(int length) => MessageCodec.IsPaddedLength(length - TagLength);
}
