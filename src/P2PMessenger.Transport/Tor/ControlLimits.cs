namespace P2PMessenger.Transport.Tor;

internal static class ControlLimits
{
    public const int MaxLineLength = 8 * 1024;
    public const int MaxDataLength = 64 * 1024;
    public const int MaxLinesPerReply = 256;
    public const int MaxArguments = 64;
}
