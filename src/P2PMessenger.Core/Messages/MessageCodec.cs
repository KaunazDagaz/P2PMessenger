using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Messages;

internal static class MessageCodec
{
    public const int LengthPrefixLength = 2;
    public const int TypeHeaderLength = 2;
    public const byte CurrentVersion = 1;
    public const int MinPaddedLength = 256;
    public const int MaxPaddedLength = 16384;
    public const int MaxContentLength = MaxPaddedLength - LengthPrefixLength;

    private static readonly int[] PaddedLengths = [MinPaddedLength, 1024, 4096, MaxPaddedLength];

    public static bool IsPaddedLength(int length) => Array.IndexOf(PaddedLengths, length) >= 0;

    public static int PaddedLengthFor(int contentLength)
    {
        int needed = LengthPrefixLength + contentLength;
        foreach (int length in PaddedLengths)
        {
            if (needed <= length)
            {
                return length;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(contentLength), "The content does not fit into the largest size class.");
    }

    public static byte[] Encode(SessionMessage message)
    {
        int contentLength = TypeHeaderLength + message.BodyLength;
        byte[] plaintext = new byte[PaddedLengthFor(contentLength)];
        BinaryPrimitives.WriteUInt16BigEndian(plaintext, (ushort)contentLength);
        plaintext[LengthPrefixLength] = (byte)message.Type;
        plaintext[LengthPrefixLength + 1] = CurrentVersion;
        message.WriteBody(plaintext.AsSpan(LengthPrefixLength + TypeHeaderLength, message.BodyLength));
        return plaintext;
    }

    public static bool TryDecode(ReadOnlySpan<byte> plaintext, [NotNullWhen(true)] out SessionMessage? message)
    {
        message = null;
        if (!IsPaddedLength(plaintext.Length))
        {
            return false;
        }

        int contentLength = BinaryPrimitives.ReadUInt16BigEndian(plaintext);
        if (contentLength < TypeHeaderLength
            || contentLength > plaintext.Length - LengthPrefixLength
            || PaddedLengthFor(contentLength) != plaintext.Length
            || plaintext[(LengthPrefixLength + contentLength)..].ContainsAnyExcept((byte)0))
        {
            return false;
        }

        ReadOnlySpan<byte> content = plaintext.Slice(LengthPrefixLength, contentLength);
        if (content[1] != CurrentVersion)
        {
            return false;
        }

        ReadOnlySpan<byte> body = content[TypeHeaderLength..];
        switch ((SessionMessageType)content[0])
        {
            case SessionMessageType.Text when TextMessage.TryDecodeBody(body, out TextMessage? text):
                message = text;
                return true;
            case SessionMessageType.Acknowledgement when Acknowledgement.TryDecodeBody(body, out Acknowledgement? acknowledgement):
                message = acknowledgement;
                return true;
            case SessionMessageType.KeepAlive when body.IsEmpty:
                message = KeepAlive.Instance;
                return true;
            default:
                return false;
        }
    }
}
