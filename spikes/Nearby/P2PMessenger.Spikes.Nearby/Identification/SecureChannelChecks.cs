using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using P2PMessenger.Spikes.Nearby.Checks;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class SecureChannelChecks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task RunAsync(CheckReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        CheckReport.Section("Connection flow over TCP on loopback: pairwise hint, Noise KK handshake, messages");
        using TestIdentity me = TestIdentity.FromName("channel-me");
        using TestIdentity friend = TestIdentity.FromName("channel-friend");
        using TestIdentity second = TestIdentity.FromName("channel-second");
        using TestIdentity stranger = TestIdentity.FromName("channel-stranger");
        using TestIdentity impostor = TestIdentity.CreateRandom("channel-friend");
        using ContactBook meBook = ContactBook.Create(me, [friend, second], cacheStaticDh: false);
        using ContactBook friendBook = ContactBook.Create(friend, [me], cacheStaticDh: false);
        using ContactBook strangerBook = ContactBook.Create(stranger, [me], cacheStaticDh: false);
        using ContactBook impostorBook = ContactBook.Create(impostor, [me], cacheStaticDh: false);
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await report.RunAsync("a contact connects, both sides are confirmed, three messages go each way", () => KnownContactAsync(listener, port, meBook, friendBook, cancellationToken)).ConfigureAwait(false);
            await report.RunAsync("a stranger is closed on without a single byte sent back", () => SilentCloseAsync(listener, port, meBook, strangerBook, cancellationToken)).ConfigureAwait(false);
            await report.RunAsync("a contact with a changed key is closed on the same way", () => SilentCloseAsync(listener, port, meBook, impostorBook, cancellationToken)).ConfigureAwait(false);
            await report.RunAsync("a changed hint is closed on without a byte sent back", () => ChangedHintAsync(listener, port, meBook, friendBook, cancellationToken)).ConfigureAwait(false);
            await report.RunAsync("a replayed first flight draws message 2, but the session is never confirmed", () => ReplayAsync(listener, port, meBook, friendBook, cancellationToken)).ConfigureAwait(false);
            await report.RunAsync("time to connect (TCP, hint, handshake) on loopback", () => TimingAsync(listener, port, meBook, friendBook, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<CheckOutcome> KnownContactAsync(TcpListener listener, int port, ContactBook server, ContactBook client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(Timeout);
        Task<ServerOutcome> serving = ServeOnceAsync(
            listener,
            server,
            async session =>
            {
                for (int i = 0; i < 3; i++)
                {
                    byte[]? received = await session.ReceiveAsync(cancellation.Token).ConfigureAwait(false);
                    if (received is null)
                    {
                        return;
                    }

                    await session.SendAsync(received, cancellation.Token).ConfigureAwait(false);
                }
            },
            cancellation.Token);

        using TcpClient tcp = new();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
        await using SecureSession? session = await SecureChannel.ConnectAsync(tcp.GetStream(), client, client.Contacts[0], cancellation.Token).ConfigureAwait(false);
        if (session is null)
        {
            return CheckOutcome.Fail("the handshake failed");
        }

        for (int i = 0; i < 3; i++)
        {
            byte[] payload = [(byte)i, 1, 2, 3, 4];
            await session.SendAsync(payload, cancellation.Token).ConfigureAwait(false);
            byte[]? echo = await session.ReceiveAsync(cancellation.Token).ConfigureAwait(false);
            if (echo is null || !echo.AsSpan().SequenceEqual(payload))
            {
                return CheckOutcome.Fail($"message {i} did not come back");
            }
        }

        ServerOutcome outcome = await serving.ConfigureAwait(false);
        return outcome.Accepted && outcome.Confirmed && outcome.PeerName == client.Self.Name && session.IsConfirmed
            ? CheckOutcome.Pass("the responder named the caller from the hint alone, before running the handshake; both sides confirmed after the first message")
            : CheckOutcome.Fail($"accepted {outcome.Accepted}, confirmed {outcome.Confirmed}, peer {outcome.PeerName}");
    }

    private static async Task<CheckOutcome> SilentCloseAsync(TcpListener listener, int port, ContactBook server, ContactBook caller, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(Timeout);
        Task<ServerOutcome> serving = ServeOnceAsync(listener, server, null, cancellation.Token);
        using TcpClient tcp = new();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
        NetworkStream stream = tcp.GetStream();
        await stream.WriteAsync(ConnectionHints.BuildNoncePreamble(caller.Contacts[0]), cancellation.Token).ConfigureAwait(false);
        await SecureChannel.WriteFrameAsync(stream, new byte[KkHandshakeLength], cancellation.Token).ConfigureAwait(false);
        int read = await ReadSomeAsync(stream, cancellation.Token).ConfigureAwait(false);
        ServerOutcome outcome = await serving.ConfigureAwait(false);
        return !outcome.Accepted && read == 0
            ? CheckOutcome.Pass("the responder closed the connection after the hint and sent nothing; the user's chats never saw it")
            : CheckOutcome.Fail($"accepted {outcome.Accepted}; bytes received back {read}");
    }

    private static async Task<CheckOutcome> ChangedHintAsync(TcpListener listener, int port, ContactBook server, ContactBook client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(Timeout);
        Task<ServerOutcome> serving = ServeOnceAsync(listener, server, null, cancellation.Token);
        using TcpClient tcp = new();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
        NetworkStream stream = tcp.GetStream();
        byte[] preamble = ConnectionHints.BuildNoncePreamble(client.Contacts[0]);
        preamble[ConnectionHints.NonceLength + 3] ^= 0x10;
        await stream.WriteAsync(preamble, cancellation.Token).ConfigureAwait(false);
        await SecureChannel.WriteFrameAsync(stream, new byte[KkHandshakeLength], cancellation.Token).ConfigureAwait(false);
        int read = await ReadSomeAsync(stream, cancellation.Token).ConfigureAwait(false);
        ServerOutcome outcome = await serving.ConfigureAwait(false);
        return !outcome.Accepted && read == 0 ? CheckOutcome.Pass("closed, nothing sent back") : CheckOutcome.Fail($"accepted {outcome.Accepted}; bytes back {read}");
    }

    private static async Task<CheckOutcome> ReplayAsync(TcpListener listener, int port, ContactBook server, ContactBook client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(Timeout);

        Task<ServerOutcome> firstServing = ServeOnceAsync(listener, server, async session => await session.ReceiveAsync(cancellation.Token).ConfigureAwait(false), cancellation.Token);
        byte[] recorded;
        using (TcpClient legitimate = new())
        {
            await legitimate.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
            CapturingStream capture = new(legitimate.GetStream());
            await using SecureSession? session = await SecureChannel.ConnectAsync(capture, client, client.Contacts[0], cancellation.Token).ConfigureAwait(false);
            if (session is null)
            {
                return CheckOutcome.Fail("the legitimate handshake failed");
            }

            await session.SendAsync(new byte[] { 1 }, cancellation.Token).ConfigureAwait(false);
            recorded = capture.Written.ToArray();
        }

        await firstServing.ConfigureAwait(false);

        Task<ServerOutcome> secondServing = ServeOnceAsync(
            listener,
            server,
            async session =>
            {
                byte[]? received = await session.ReceiveAsync(cancellation.Token).ConfigureAwait(false);
                if (received is not null)
                {
                    throw new InvalidOperationException("The replayer's message was accepted.");
                }
            },
            cancellation.Token);
        using TcpClient replayer = new();
        await replayer.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
        NetworkStream stream = replayer.GetStream();
        await stream.WriteAsync(recorded, cancellation.Token).ConfigureAwait(false);
        byte[]? reply = await SecureChannel.ReadFrameAsync(stream, SecureChannel.MaxHandshakeFrame, cancellation.Token).ConfigureAwait(false);
        await SecureChannel.WriteFrameAsync(stream, new byte[40], cancellation.Token).ConfigureAwait(false);
        ServerOutcome outcome = await secondServing.ConfigureAwait(false);
        return reply is not null && outcome.Accepted && !outcome.Confirmed
            ? CheckOutcome.Info("the responder answered the replayed message 1 with a message 2 (Noise KK cannot tell a replay from the original), but the replayer could not send a valid transport message, so the session stayed unconfirmed: the app must show a contact as connected only after the first transport message decrypts")
            : CheckOutcome.Fail($"reply {(reply is null ? "none" : "received")}, accepted {outcome.Accepted}, confirmed {outcome.Confirmed}");
    }

    private static async Task<CheckOutcome> TimingAsync(TcpListener listener, int port, ContactBook server, ContactBook client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(60));
        const int rounds = 20;
        double[] times = new double[rounds];
        for (int i = 0; i < rounds; i++)
        {
            Task<ServerOutcome> serving = ServeOnceAsync(listener, server, async session => await session.ReceiveAsync(cancellation.Token).ConfigureAwait(false), cancellation.Token);
            long start = Stopwatch.GetTimestamp();
            using TcpClient tcp = new();
            await tcp.ConnectAsync(IPAddress.Loopback, port, cancellation.Token).ConfigureAwait(false);
            await using SecureSession? session = await SecureChannel.ConnectAsync(tcp.GetStream(), client, client.Contacts[0], cancellation.Token).ConfigureAwait(false);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (session is null)
            {
                return CheckOutcome.Fail("a handshake failed");
            }

            await session.SendAsync(new byte[] { 1 }, cancellation.Token).ConfigureAwait(false);
            await serving.ConfigureAwait(false);
        }

        Array.Sort(times);
        return CheckOutcome.Info(string.Create(CultureInfo.InvariantCulture, $"median {times[rounds / 2]:F1} ms, slowest {times[^1]:F1} ms over {rounds} connections, both parties in one process"));
    }

    private static int KkHandshakeLength => 48;

    private static async Task<int> ReadSomeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64];
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static Task<ServerOutcome> ServeOnceAsync(TcpListener listener, ContactBook book, Func<SecureSession, Task>? onSession, CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                NetworkStream stream = accepted.GetStream();
                SecureSession? session = await SecureChannel.AcceptAsync(stream, book, cancellationToken).ConfigureAwait(false);
                if (session is null)
                {
                    return new ServerOutcome(false, false, null);
                }

                await using (session.ConfigureAwait(false))
                {
                    if (onSession is not null)
                    {
                        await onSession(session).ConfigureAwait(false);
                    }

                    return new ServerOutcome(true, session.IsConfirmed, session.Peer.Name);
                }
            },
            cancellationToken);
    }

    private sealed record ServerOutcome(bool Accepted, bool Confirmed, string? PeerName);

    private sealed class CapturingStream(Stream inner) : Stream
    {
        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Written.Write(buffer, offset, count);
            inner.Write(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.Write(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    }
}
