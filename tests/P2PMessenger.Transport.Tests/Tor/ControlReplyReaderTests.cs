using System.Text;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

public sealed class ControlReplyReaderTests
{
    [Fact]
    public async Task ReadAsync_SingleLine_GivesTheStatusAndText()
    {
        var reader = Reader("250 OK\r\n");

        var reply = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.Equal(250, reply.Status);
        Assert.True(reply.IsSuccess);
        Assert.False(reply.IsEvent);
        Assert.Equal("OK", reply.FirstLine);
    }

    [Fact]
    public async Task ReadAsync_MultipleLines_GivesEveryLineInOrder()
    {
        var reader = Reader("250-ServiceID=abc\r\n250-version=0.4.9.16\r\n250 OK\r\n");

        var reply = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.Equal(["ServiceID=abc", "version=0.4.9.16", "OK"], reply.Lines.Select(static line => line.Text));
    }

    [Fact]
    public async Task ReadAsync_DataBlock_RemovesTheDotEscapes()
    {
        var reader = Reader("250+config-text=\r\nSocksPort auto\r\n..hidden\r\n.\r\n250 OK\r\n");

        var reply = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.Equal("config-text=", reply.Lines[0].Text);
        Assert.Equal("SocksPort auto\n.hidden", reply.Lines[0].Data);
    }

    [Fact]
    public async Task ReadAsync_EventsAndRepliesInTurn_GivesEachOnItsOwn()
    {
        var reader = Reader("650 STATUS_CLIENT NOTICE CIRCUIT_ESTABLISHED\r\n250 OK\r\n");
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await reader.ReadAsync(cancellationToken);
        var second = await reader.ReadAsync(cancellationToken);
        var end = await reader.ReadAsync(cancellationToken);

        Assert.True(first!.IsEvent);
        Assert.True(second!.IsSuccess);
        Assert.Null(end);
    }

    [Fact]
    public async Task ReadAsync_LineFeedWithoutCarriageReturn_IsAccepted()
    {
        var reply = await Reader("250 OK\n").ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("OK", reply!.FirstLine);
    }

    [Fact]
    public async Task ReadAsync_ByteByByte_GivesTheSameReply()
    {
        var reader = new ControlReplyReader(new TrickleStream(Encoding.ASCII.GetBytes("250-a=1\r\n250 OK\r\n")));

        var reply = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, reply!.Lines.Count);
    }

    [Theory]
    [InlineData("25 OK\r\n")]
    [InlineData("2x0 OK\r\n")]
    [InlineData("250\r\n")]
    [InlineData("250*OK\r\n")]
    [InlineData("150 OK\r\n")]
    [InlineData("700 OK\r\n")]
    [InlineData("250-a\r\n251 OK\r\n")]
    [InlineData("250-a\r\n")]
    [InlineData("250 O")]
    [InlineData("250+x=\r\nno end\r\n")]
    [InlineData("250 \u0001OK\r\n")]
    [InlineData("250 \u00e9\r\n")]
    public async Task ReadAsync_MalformedInput_Throws(string text)
    {
        var reader = new ControlReplyReader(new MemoryStream(Encoding.Latin1.GetBytes(text)));

        await Assert.ThrowsAsync<TorControlException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_LineAboveTheLimit_Throws()
    {
        var reader = Reader("250 " + new string('a', 8 * 1024) + "\r\n");

        await Assert.ThrowsAsync<TorControlException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_LineAtTheLimit_IsAccepted()
    {
        var reader = Reader("250 " + new string('a', (8 * 1024) - 4) + "\r\n");

        var reply = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal((8 * 1024) - 4, reply!.FirstLine.Length);
    }

    [Fact]
    public async Task ReadAsync_EndlessLineWithoutNewline_ThrowsBeforeReadingItAll()
    {
        var reader = new ControlReplyReader(new EndlessStream((byte)'a'));

        await Assert.ThrowsAsync<TorControlException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_TooManyLines_Throws()
    {
        var text = new StringBuilder();
        for (var line = 0; line < 257; line++)
        {
            text.Append("250-a\r\n");
        }

        text.Append("250 OK\r\n");

        await Assert.ThrowsAsync<TorControlException>(async () => await Reader(text.ToString()).ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_DataAboveTheLimit_Throws()
    {
        var text = new StringBuilder("250+x=\r\n");
        for (var line = 0; line < 70; line++)
        {
            text.Append(new string('a', 1000)).Append("\r\n");
        }

        text.Append(".\r\n250 OK\r\n");

        await Assert.ThrowsAsync<TorControlException>(async () => await Reader(text.ToString()).ReadAsync(TestContext.Current.CancellationToken));
    }

    private static ControlReplyReader Reader(string text) => new(new MemoryStream(Encoding.ASCII.GetBytes(text)));

    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= data.Length || count == 0)
            {
                return 0;
            }

            buffer[offset] = data[position++];
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class EndlessStream(byte value) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (BytesRead > 1024 * 1024)
            {
                throw new InvalidOperationException("The reader read far beyond its limit.");
            }

            Array.Fill(buffer, value, offset, count);
            BytesRead += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
