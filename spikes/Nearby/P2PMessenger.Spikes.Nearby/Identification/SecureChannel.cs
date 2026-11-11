using System.Buffers.Binary;
using System.Text;
using P2PMessenger.Spikes.Nearby.Noise;

namespace P2PMessenger.Spikes.Nearby.Identification;

public sealed class SecureSession : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly KkHandshake _handshake;

    internal SecureSession(Stream stream, KkHandshake handshake, Contact peer, bool confirmed)
    {
        _stream = stream;
        _handshake = handshake;
        Peer = peer;
        IsConfirmed = confirmed;
    }

    public Contact Peer { get; }

    public bool IsConfirmed { get; private set; }

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        byte[] message = new byte[payload.Length + 16];
        int written = _handshake.Transport.WriteMessage(payload.Span, message);
        await SecureChannel.WriteFrameAsync(_stream, message.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[]? frame = await SecureChannel.ReadFrameAsync(_stream, KkHandshake.MaxMessageLength, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        byte[] payload = new byte[Math.Max(0, frame.Length - 16)];
        if (!_handshake.Transport.TryReadMessage(frame, payload, out int length))
        {
            return null;
        }

        IsConfirmed = true;
        return payload[..length];
    }

    public ValueTask DisposeAsync()
    {
        _handshake.Dispose();
        return _stream.DisposeAsync();
    }
}

public static class SecureChannel
{
    public const int MaxHandshakeFrame = 1024;

    private static readonly byte[] PrologueLabel = Encoding.ASCII.GetBytes("P2PM/s05/kk/v1");

    public static async Task<SecureSession?> ConnectAsync(Stream stream, ContactBook book, Contact target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(target);
        byte[] preamble = ConnectionHints.BuildNoncePreamble(target);
        KkHandshake handshake = new(NoiseProtocol.KkBlake2b, initiator: true, Prologue(preamble), book.Self.StaticKey, target.PublicKey);
        try
        {
            byte[] message1 = new byte[KkHandshake.MessageOverhead];
            int length = handshake.WriteMessage(default, message1);
            byte[] first = new byte[preamble.Length + 2 + length];
            preamble.CopyTo(first, 0);
            BinaryPrimitives.WriteUInt16BigEndian(first.AsSpan(preamble.Length), (ushort)length);
            message1.AsSpan(0, length).CopyTo(first.AsSpan(preamble.Length + 2));
            await stream.WriteAsync(first, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            byte[]? message2 = await ReadFrameAsync(stream, MaxHandshakeFrame, cancellationToken).ConfigureAwait(false);
            if (message2 is null || !handshake.TryReadMessage(message2, new byte[message2.Length], out _))
            {
                handshake.Dispose();
                return null;
            }

            return new SecureSession(stream, handshake, target, confirmed: true);
        }
        catch
        {
            handshake.Dispose();
            throw;
        }
    }

    public static async Task<SecureSession?> AcceptAsync(Stream stream, ContactBook book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(book);
        byte[]? preamble = await ReadExactAsync(stream, ConnectionHints.PreambleLength, cancellationToken).ConfigureAwait(false);
        if (preamble is null || !ConnectionHints.TryIdentifyNonce(book, preamble, out Contact? caller, out _) || caller is null)
        {
            return null;
        }

        byte[]? message1 = await ReadFrameAsync(stream, MaxHandshakeFrame, cancellationToken).ConfigureAwait(false);
        if (message1 is null)
        {
            return null;
        }

        KkHandshake handshake = new(NoiseProtocol.KkBlake2b, initiator: false, Prologue(preamble), book.Self.StaticKey, caller.PublicKey);
        try
        {
            if (!handshake.TryReadMessage(message1, new byte[message1.Length], out _))
            {
                handshake.Dispose();
                return null;
            }

            byte[] message2 = new byte[KkHandshake.MessageOverhead];
            int length = handshake.WriteMessage(default, message2);
            await WriteFrameAsync(stream, message2.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return new SecureSession(stream, handshake, caller, confirmed: false);
        }
        catch
        {
            handshake.Dispose();
            throw;
        }
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[2 + frame.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)frame.Length);
        frame.CopyTo(buffer.AsMemory(2));
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxLength, CancellationToken cancellationToken)
    {
        byte[]? header = await ReadExactAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(header);
        return length == 0 || length > maxLength ? null : await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }

        return buffer;
    }

    private static byte[] Prologue(byte[] preamble)
    {
        byte[] prologue = new byte[PrologueLabel.Length + preamble.Length];
        PrologueLabel.CopyTo(prologue, 0);
        preamble.CopyTo(prologue, PrologueLabel.Length);
        return prologue;
    }
}
