using System.Text;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

[Trait("Story", "US-09")]
public sealed class Socks5ClientTests
{
    private const string Host = "abcdefghijklmnopqrstuvwxyz234567abcdefghijklmnopqrstuvwx.onion";
    private const int Port = 45821;
    private const int HeaderLength = 8;

    private static readonly byte[] Isolation = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
    private static readonly byte[] MethodChosen = [5, 2];
    private static readonly byte[] Authenticated = [1, 0];
    private static readonly byte[] SucceededIPv4 = [5, 0, 0, 1, 127, 0, 0, 1, 0xB3, 0x1D];

    [Fact]
    public async Task Handshake_ProxyAccepts_SendsGreetingAuthenticationAndConnectAndSucceeds()
    {
        var stream = new ScriptedStream([.. MethodChosen, .. Authenticated, .. SucceededIPv4]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.None, failure);
        byte[] expected =
        [
            5, 1, 2,
            1, 32, .. Isolation, 4, (byte)'p', (byte)'2', (byte)'p', (byte)'m',
            5, 1, 0, 3, (byte)Host.Length, .. Encoding.ASCII.GetBytes(Host), 0xB2, 0xFD,
        ];
        Assert.Equal(expected, stream.Written);
    }

    [Theory]
    [InlineData(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 80 })]
    [InlineData(new byte[] { 5, 0, 0, 3, 3, (byte)'a', (byte)'.', (byte)'b', 0, 80 })]
    public async Task Handshake_BoundAddressOfEachType_ReadsExactlyTheReply(byte[] reply)
    {
        byte[] after = [0xCA, 0xFE];
        var stream = new ScriptedStream([.. MethodChosen, .. Authenticated, .. reply, .. after]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.None, failure);
        Assert.Equal(after, stream.Remaining);
    }

    [Fact]
    public async Task Handshake_ProxyAnswersOneByteAtATime_Succeeds()
    {
        var stream = new ScriptedStream([.. MethodChosen, .. Authenticated, .. SucceededIPv4]) { OneByteAtATime = true };

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.None, failure);
    }

    [Theory]
    [InlineData(0x01, SocksFailure.GeneralFailure)]
    [InlineData(0x02, SocksFailure.NotAllowed)]
    [InlineData(0x03, SocksFailure.NetworkUnreachable)]
    [InlineData(0x04, SocksFailure.HostUnreachable)]
    [InlineData(0x05, SocksFailure.ConnectionRefused)]
    [InlineData(0x06, SocksFailure.TtlExpired)]
    [InlineData(0x07, SocksFailure.CommandNotSupported)]
    [InlineData(0x08, SocksFailure.AddressTypeNotSupported)]
    [InlineData(0x09, SocksFailure.UnknownReply)]
    [InlineData(0xEF, SocksFailure.UnknownReply)]
    [InlineData(0xF0, SocksFailure.OnionDescriptorNotFound)]
    [InlineData(0xF1, SocksFailure.OnionDescriptorInvalid)]
    [InlineData(0xF2, SocksFailure.OnionIntroductionFailed)]
    [InlineData(0xF3, SocksFailure.OnionRendezvousFailed)]
    [InlineData(0xF4, SocksFailure.OnionClientAuthorizationMissing)]
    [InlineData(0xF5, SocksFailure.OnionClientAuthorizationWrong)]
    [InlineData(0xF6, SocksFailure.OnionAddressInvalid)]
    [InlineData(0xF7, SocksFailure.OnionIntroductionTimedOut)]
    [InlineData(0xF8, SocksFailure.UnknownReply)]
    [InlineData(0xFF, SocksFailure.UnknownReply)]
    public async Task Handshake_ProxyRefusesTheConnection_ReportsTheReply(byte reply, SocksFailure expected)
    {
        var stream = new ScriptedStream([.. MethodChosen, .. Authenticated, 5, reply, 0, 1, 0, 0, 0, 0, 0, 0]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(expected, failure);
    }

    [Theory]
    [InlineData(new byte[] { 4, 2 })]
    [InlineData(new byte[] { 5, 0 })]
    [InlineData(new byte[] { 5, 0xFF })]
    public async Task Handshake_UnexpectedMethodReply_IsAProtocolErrorAndSendsNoCredentials(byte[] methodReply)
    {
        var stream = new ScriptedStream([.. methodReply, .. Authenticated, .. SucceededIPv4]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.ProtocolError, failure);
        Assert.Equal([5, 1, 2], stream.Written);
    }

    [Theory]
    [InlineData(new byte[] { 2, 0 })]
    [InlineData(new byte[] { 1, 1 })]
    public async Task Handshake_AuthenticationRefused_IsAProtocolErrorAndSendsNoConnectRequest(byte[] authenticationReply)
    {
        var stream = new ScriptedStream([.. MethodChosen, .. authenticationReply, .. SucceededIPv4]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.ProtocolError, failure);
        Assert.Equal(3 + 39, stream.Written.Length);
    }

    [Theory]
    [InlineData(new byte[] { 4, 0, 0, 1, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 1, 1, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 2, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 5, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 3, 0, 0, 0 })]
    public async Task Handshake_MalformedConnectReply_IsAProtocolError(byte[] reply)
    {
        var stream = new ScriptedStream([.. MethodChosen, .. Authenticated, .. reply]);

        var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

        Assert.Equal(SocksFailure.ProtocolError, failure);
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public async Task Handshake_ReplyCutAtEveryByte_ReportsThatTheProxyClosed()
    {
        byte[] full = [.. MethodChosen, .. Authenticated, .. SucceededIPv4];
        for (int length = 0; length < full.Length; length++)
        {
            var stream = new ScriptedStream(full[..length]);

            var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

            Assert.Equal(SocksFailure.ProxyClosed, failure);
        }
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public async Task Handshake_EveryBitOfTheRepliesFlipped_NeverSucceedsUnlessOnlyTheIgnoredBoundAddressChanged()
    {
        byte[] full = [.. MethodChosen, .. Authenticated, .. SucceededIPv4];
        for (int index = 0; index < full.Length; index++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] flipped = [.. full];
                flipped[index] ^= (byte)(1 << bit);
                var stream = new ScriptedStream(flipped);

                var failure = await Socks5Client.HandshakeAsync(stream, Host, Port, Isolation, TestContext.Current.CancellationToken);

                if (index < HeaderLength)
                {
                    Assert.NotEqual(SocksFailure.None, failure);
                }
                else
                {
                    Assert.Equal(SocksFailure.None, failure);
                }
            }
        }
    }

    [Fact]
    public void NewIsolation_GivesADifferentTokenOfHexDigitsEachTime()
    {
        var first = Encoding.ASCII.GetString(Socks5Client.NewIsolation());
        var second = Encoding.ASCII.GetString(Socks5Client.NewIsolation());

        Assert.Equal(Socks5Client.IsolationLength, first.Length);
        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Authentication_TokenOfWrongLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => Socks5Client.Authentication(new byte[length]));
    }

    [Fact]
    public void ConnectRequest_HostOf255Characters_IsAcceptedAndOneMoreIsNot()
    {
        var longest = new string('a', Socks5Client.MaxHostLength);

        Assert.Equal(7 + 255, Socks5Client.ConnectRequest(longest, Port).Length);
        Assert.Throws<ArgumentException>(() => Socks5Client.ConnectRequest(longest + "a", Port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC.onion")]
    [InlineData("a b.onion")]
    [InlineData("aé.onion")]
    public void ConnectRequest_HostThatTorDoesNotTake_Throws(string host)
    {
        Assert.Throws<ArgumentException>(() => Socks5Client.ConnectRequest(host, Port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ConnectRequest_PortOutOfRange_Throws(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Socks5Client.ConnectRequest(Host, port));
    }

    private sealed class ScriptedStream(byte[] replies) : Stream
    {
        private readonly MemoryStream written = new();
        private int position;

        public bool OneByteAtATime { get; init; }

        public byte[] Written => written.ToArray();

        public byte[] Remaining => replies[position..];

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int available = Math.Min(buffer.Length, replies.Length - position);
            if (OneByteAtATime)
            {
                available = Math.Min(available, 1);
            }

            replies.AsSpan(position, available).CopyTo(buffer);
            position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => written.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                written.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
