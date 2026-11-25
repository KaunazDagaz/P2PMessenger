using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace P2PMessenger.Transport.Tor;

internal static class Socks5Client
{
    public const int MaxHostLength = 255;
    public const int IsolationLength = 32;

    private const byte Version = 0x05;
    private const byte UsernamePassword = 0x02;
    private const byte NoAcceptableMethod = 0xFF;
    private const byte AuthenticationVersion = 0x01;
    private const byte Connect = 0x01;
    private const byte IPv4 = 0x01;
    private const byte DomainName = 0x03;
    private const byte IPv6 = 0x04;
    private const byte Succeeded = 0x00;

    private static ReadOnlySpan<byte> Password => "p2pm"u8;

    public static byte[] NewIsolation()
    {
        Span<byte> random = stackalloc byte[IsolationLength / 2];
        RandomNumberGenerator.Fill(random);
        return Encoding.ASCII.GetBytes(Convert.ToHexStringLower(random));
    }

    public static bool IsValidHost(string host) =>
        host.Length is > 0 and <= MaxHostLength && host.All(static character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-');

    public static byte[] Greeting() => [Version, 1, UsernamePassword];

    public static byte[] Authentication(ReadOnlySpan<byte> isolation)
    {
        if (isolation.Length is 0 or > byte.MaxValue)
        {
            throw new ArgumentException("An isolation token has 1 to 255 bytes.", nameof(isolation));
        }

        byte[] request = new byte[3 + isolation.Length + Password.Length];
        request[0] = AuthenticationVersion;
        request[1] = (byte)isolation.Length;
        isolation.CopyTo(request.AsSpan(2));
        request[2 + isolation.Length] = (byte)Password.Length;
        Password.CopyTo(request.AsSpan(3 + isolation.Length));
        return request;
    }

    public static byte[] ConnectRequest(string host, int port)
    {
        if (!IsValidHost(host))
        {
            throw new ArgumentException("The host name is not one that tor's SOCKS port takes.", nameof(host));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        byte[] request = new byte[7 + host.Length];
        request[0] = Version;
        request[1] = Connect;
        request[2] = 0;
        request[3] = DomainName;
        request[4] = (byte)host.Length;
        Encoding.ASCII.GetBytes(host, request.AsSpan(5));
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5 + host.Length), (ushort)port);
        return request;
    }

    public static SocksFailure FailureOf(byte reply) => reply switch
    {
        Succeeded => SocksFailure.None,
        0x01 => SocksFailure.GeneralFailure,
        0x02 => SocksFailure.NotAllowed,
        0x03 => SocksFailure.NetworkUnreachable,
        0x04 => SocksFailure.HostUnreachable,
        0x05 => SocksFailure.ConnectionRefused,
        0x06 => SocksFailure.TtlExpired,
        0x07 => SocksFailure.CommandNotSupported,
        0x08 => SocksFailure.AddressTypeNotSupported,
        0xF0 => SocksFailure.OnionDescriptorNotFound,
        0xF1 => SocksFailure.OnionDescriptorInvalid,
        0xF2 => SocksFailure.OnionIntroductionFailed,
        0xF3 => SocksFailure.OnionRendezvousFailed,
        0xF4 => SocksFailure.OnionClientAuthorizationMissing,
        0xF5 => SocksFailure.OnionClientAuthorizationWrong,
        0xF6 => SocksFailure.OnionAddressInvalid,
        0xF7 => SocksFailure.OnionIntroductionTimedOut,
        _ => SocksFailure.UnknownReply,
    };

    public static async Task<SocksFailure> HandshakeAsync(Stream stream, string host, int port, byte[] isolation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(isolation);

        byte[] connect = ConnectRequest(host, port);
        byte[] authentication = Authentication(isolation);
        byte[] reply = new byte[4];
        try
        {
            await stream.WriteAsync(Greeting(), cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(reply.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
            if (reply[0] != Version || reply[1] != UsernamePassword)
            {
                return SocksFailure.ProtocolError;
            }

            await stream.WriteAsync(authentication, cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(reply.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
            if (reply[0] != AuthenticationVersion || reply[1] != Succeeded)
            {
                return SocksFailure.ProtocolError;
            }

            await stream.WriteAsync(connect, cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(reply, cancellationToken).ConfigureAwait(false);
            if (reply[0] != Version)
            {
                return SocksFailure.ProtocolError;
            }

            if (reply[1] != Succeeded)
            {
                return FailureOf(reply[1]);
            }

            if (reply[2] != 0)
            {
                return SocksFailure.ProtocolError;
            }

            int remaining;
            switch (reply[3])
            {
                case IPv4:
                    remaining = 4 + 2;
                    break;
                case IPv6:
                    remaining = 16 + 2;
                    break;
                case DomainName:
                    await stream.ReadExactlyAsync(reply.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                    if (reply[0] == 0)
                    {
                        return SocksFailure.ProtocolError;
                    }

                    remaining = reply[0] + 2;
                    break;
                default:
                    return SocksFailure.ProtocolError;
            }

            byte[] bound = new byte[remaining];
            await stream.ReadExactlyAsync(bound, cancellationToken).ConfigureAwait(false);
            return SocksFailure.None;
        }
        catch (EndOfStreamException)
        {
            return SocksFailure.ProxyClosed;
        }
    }
}
