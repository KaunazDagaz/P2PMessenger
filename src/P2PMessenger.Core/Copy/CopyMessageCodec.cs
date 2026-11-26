using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Copy;

internal static class CopyMessageCodec
{
    public const int LengthPrefixLength = 2;
    public const int TypeHeaderLength = 2;
    public const byte CurrentVersion = 1;
    public const int MinPaddedLength = 256;
    public const int MaxPaddedLength = 32768;
    public const int MaxContentLength = MaxPaddedLength - LengthPrefixLength;

    private static readonly int[] PaddedLengths = [MinPaddedLength, 1024, 4096, 16384, MaxPaddedLength];

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

        throw new ArgumentOutOfRangeException(nameof(contentLength), "The content does not fit into the largest copy size class.");
    }

    public static byte[] Encode(CopyMessage message)
    {
        int contentLength = TypeHeaderLength + message.BodyLength;
        byte[] plaintext = new byte[PaddedLengthFor(contentLength)];
        BinaryPrimitives.WriteUInt16BigEndian(plaintext, (ushort)contentLength);
        plaintext[LengthPrefixLength] = (byte)message.Type;
        plaintext[LengthPrefixLength + 1] = CurrentVersion;
        message.WriteBody(plaintext.AsSpan(LengthPrefixLength + TypeHeaderLength, message.BodyLength));
        return plaintext;
    }

    public static bool TryDecode(ReadOnlySpan<byte> plaintext, [NotNullWhen(true)] out CopyMessage? message)
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
        switch ((CopyMessageType)content[0])
        {
            case CopyMessageType.Ready when body.IsEmpty:
                message = CopyReady.Instance;
                return true;
            case CopyMessageType.KeepAlive when body.IsEmpty:
                message = CopyKeepAlive.Instance;
                return true;
            case CopyMessageType.TransferStart when CopyTransferStart.TryDecodeBody(body, out CopyTransferStart? start):
                message = start;
                return true;
            case CopyMessageType.ConversationStart when CopyConversationStart.TryDecodeBody(body, out CopyConversationStart? conversationStart):
                message = conversationStart;
                return true;
            case CopyMessageType.ConversationMessage when CopyConversationMessage.TryDecodeBody(body, out CopyConversationMessage? conversationMessage):
                message = conversationMessage;
                return true;
            case CopyMessageType.ConversationEnd when CopyConversationEnd.TryDecodeBody(body, out CopyConversationEnd? conversationEnd):
                message = conversationEnd;
                return true;
            case CopyMessageType.ConversationSaved when CopyConversationSaved.TryDecodeBody(body, out CopyConversationSaved? conversationSaved):
                message = conversationSaved;
                return true;
            case CopyMessageType.TransferDone when CopyTransferDone.TryDecodeBody(body, out CopyTransferDone? done):
                message = done;
                return true;
            case CopyMessageType.TransferComplete when CopyTransferComplete.TryDecodeBody(body, out CopyTransferComplete? complete):
                message = complete;
                return true;
            default:
                return false;
        }
    }
}
