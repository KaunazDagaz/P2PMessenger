using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Copy;

public sealed class CopyListener : IAsyncDisposable
{
    private const int Backlog = 16;

    private readonly TcpListener listener;
    private readonly X25519KeyPair keyPair;
    private readonly CopyOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly IFailureSink? failures;
    private readonly AdmissionControl admission;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly HashSet<Task> handlers = [];
    private readonly TaskCompletionSource<CopyChannel?> accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task acceptLoop = Task.CompletedTask;
    private long lastNumber;
    private int claimed;
    private int disposed;

    private CopyListener(
        TcpListener listener,
        X25519KeyPair keyPair,
        CopyPairingCode pairingCode,
        CopyOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        IFailureSink? failures)
    {
        this.listener = listener;
        this.keyPair = keyPair;
        PairingCode = pairingCode;
        LocalEndpoint = (IPEndPoint)listener.LocalEndpoint;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.failures = failures;
        admission = new AdmissionControl(options.Admission, timeProvider);
    }

    public IPEndPoint LocalEndpoint { get; }

    public CopyPairingCode PairingCode { get; }

    public static CopyListener Start(
        IPEndPoint listenEndpoint,
        IReadOnlyCollection<Ipv4Address> advertisedAddresses,
        CopyOptions options,
        TimeProvider timeProvider,
        ILogger<CopyListener> logger,
        IFailureSink? failures = null)
    {
        ArgumentNullException.ThrowIfNull(listenEndpoint);
        ArgumentNullException.ThrowIfNull(advertisedAddresses);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        string? problem = options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        X25519KeyPair keyPair = X25519KeyPair.Create();
        TcpListener? tcp = null;
        byte[] secret = new byte[CopyPairingCode.SecretLength];
        try
        {
            FillNonZero(secret);
            tcp = new TcpListener(listenEndpoint);
            tcp.Start(Backlog);
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            CopyPairingCode pairingCode = CopyPairingCode.Create(keyPair.PublicKey, secret, port, advertisedAddresses);
            CopyListener started = new(tcp, keyPair, pairingCode, options, timeProvider, logger, failures);
            tcp = null;
            logger.CopyListening(port);
            started.acceptLoop = started.AcceptLoopAsync(started.stop.Token);
            return started;
        }
        catch
        {
            tcp?.Dispose();
            keyPair.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public async Task<CopyChannel?> AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await accepted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        listener.Dispose();
        accepted.TrySetResult(null);
        await acceptLoop.ConfigureAwait(false);
        await Task.WhenAll(SnapshotHandlers()).ConfigureAwait(false);
        stop.Dispose();
        keyPair.Dispose();
    }

    private static void FillNonZero(Span<byte> buffer)
    {
        do
        {
            RandomNumberGenerator.Fill(buffer);
        }
        while (!buffer.ContainsAnyExcept((byte)0));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref claimed) == 0)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            catch (SocketException exception)
            {
                logger.AcceptFailed(exception.SocketErrorCode);
                return;
            }

            Track(HandleAsync(socket, cancellationToken));
        }
    }

    private async Task HandleAsync(Socket socket, CancellationToken cancellationToken)
    {
        long number = Interlocked.Increment(ref lastNumber);
        IPAddress? source;
        NetworkStream stream;
        try
        {
            socket.NoDelay = true;
            source = (socket.RemoteEndPoint as IPEndPoint)?.Address;
            stream = new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException)
        {
            socket.Dispose();
            return;
        }

        if (Volatile.Read(ref claimed) != 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (!admission.TryAdmit(source, out AdmissionSlot? slot, out AdmissionRefusal refusal))
        {
            logger.CopyNotAdmitted(number, refusal);
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        long started = timeProvider.GetTimestamp();
        CopySession? session = null;
        try
        {
            using CancellationTokenSource handshaking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshaking.CancelAfter(options.HandshakeTimeout);

            byte[] firstMessage = new byte[CopyFrame.HandshakeFrameLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, firstMessage, handshaking.Token).ConfigureAwait(false))
            {
                logger.CopyRefused(number, HandshakeRefusal.Closed);
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (!CopyResponder.TryAccept(keyPair, PairingCode, firstMessage, out session, out byte[]? response))
            {
                logger.CopyRefused(number, HandshakeRefusal.HandshakeFailed);
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            await stream.WriteAsync(response, handshaking.Token).ConfigureAwait(false);

            byte[] header = new byte[CopyFrame.HeaderLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, header, handshaking.Token).ConfigureAwait(false)
                || !CopyFrame.TryReadTransportLength(header, out int bodyLength))
            {
                logger.CopyRefused(number, HandshakeRefusal.NotConfirmed);
                session.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            byte[] body = new byte[bodyLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, body, handshaking.Token).ConfigureAwait(false)
                || !session.TryReadFrame(body, out CopyMessage? first)
                || first is not CopyReady)
            {
                logger.CopyRefused(number, HandshakeRefusal.NotConfirmed);
                session.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (Interlocked.CompareExchange(ref claimed, 1, 0) != 0)
            {
                session.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            byte[] readyReply = session.WriteFrame(CopyReady.Instance);
            await stream.WriteAsync(readyReply, handshaking.Token).ConfigureAwait(false);

            CopyChannel channel = new(stream, session, options, timeProvider, logger);
            session = null;
            listener.Dispose();
            long elapsed = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            logger.CopyAccepted(number, elapsed);
            if (!accepted.TrySetResult(channel))
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            logger.CopyRefused(number, HandshakeRefusal.TimedOut);
            session?.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            logger.CopyRefused(number, HandshakeRefusal.Closed);
            session?.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            ReportUnexpected(exception);
            session?.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            slot.Dispose();
        }
    }

    private void ReportUnexpected(Exception exception)
    {
        if (failures is not null)
        {
            failures.Report(FailureSource.PcCopy, exception);
            return;
        }

        logger.CopyUnexpected(Failures.TypeName(exception));
    }

    private void Track(Task handler)
    {
        lock (gate)
        {
            handlers.Add(handler);
        }

        _ = handler.ContinueWith(
            static (finished, state) => ((CopyListener)state!).Untrack(finished),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Untrack(Task handler)
    {
        lock (gate)
        {
            handlers.Remove(handler);
        }
    }

    private Task[] SnapshotHandlers()
    {
        lock (gate)
        {
            return [.. handlers];
        }
    }
}
