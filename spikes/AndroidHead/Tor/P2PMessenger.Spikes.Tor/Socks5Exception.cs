using System.Globalization;

namespace P2PMessenger.Spikes.Tor;

public sealed class Socks5Exception : Exception
{
    public Socks5Exception()
    {
    }

    public Socks5Exception(string message)
        : base(message)
    {
    }

    public Socks5Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Socks5Exception(byte replyCode)
        : base(Describe(replyCode))
    {
        ReplyCode = replyCode;
    }

    public byte? ReplyCode { get; }

    public static string Describe(byte replyCode)
    {
        var text = replyCode switch
        {
            0x01 => "general SOCKS server failure",
            0x02 => "connection not allowed by ruleset",
            0x03 => "network unreachable",
            0x04 => "host unreachable",
            0x05 => "connection refused",
            0x06 => "TTL expired",
            0x07 => "command not supported",
            0x08 => "address type not supported",
            0xF0 => "onion service descriptor not found",
            0xF1 => "onion service descriptor is invalid",
            0xF2 => "onion service introduction failed",
            0xF3 => "onion service rendezvous failed",
            0xF4 => "onion service needs client authorization",
            0xF5 => "onion service client authorization is wrong",
            0xF6 => "onion address is invalid",
            0xF7 => "onion service introduction timed out",
            _ => "unknown reply code",
        };

        return string.Create(CultureInfo.InvariantCulture, $"SOCKS5 reply 0x{replyCode:X2}: {text}");
    }
}
