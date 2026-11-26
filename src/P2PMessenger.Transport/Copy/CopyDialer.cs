using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Copy;

public static class CopyDialer
{
    public static async Task<CopyConnectResult> ConnectAsync(
        CopyPairingCode pairingCode,
        CopyOptions options,
        TimeProvider timeProvider,
        ILogger<CopyChannel> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pairingCode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        string? problem = options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        using CancellationTokenSource winnerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TaskCompletionSource<CopyChannel> winner = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CopyConnectFailure>[] attempts = new Task<CopyConnectFailure>[pairingCode.Addresses.Count];
        for (int index = 0; index < pairingCode.Addresses.Count; index++)
        {
            IPAddress address = new(pairingCode.Addresses[index].ToBytes());
            IPEndPoint endpoint = new(address, pairingCode.Port);
            attempts[index] = AttemptAsync(endpoint, pairingCode, options, timeProvider, logger, winner, winnerStop, cancellationToken);
        }

        Task<CopyConnectFailure[]> allDone = Task.WhenAll(attempts);
        Task first = await Task.WhenAny(winner.Task, allDone).ConfigureAwait(false);
        if (ReferenceEquals(first, winner.Task))
        {
            await winnerStop.CancelAsync().ConfigureAwait(false);
            _ = await allDone.ConfigureAwait(false);
            return CopyConnectResult.Connected(await winner.Task.ConfigureAwait(false));
        }

        CopyConnectFailure[] failures = await allDone.ConfigureAwait(false);
        if (winner.Task.IsCompletedSuccessfully)
        {
            return CopyConnectResult.Connected(await winner.Task.ConfigureAwait(false));
        }

        CopyConnectFailure outcome = ChooseFailure(failures, cancellationToken.IsCancellationRequested);
        logger.CopyDialFailed(outcome);
        return CopyConnectResult.Failed(outcome);
    }

    private static CopyConnectFailure ChooseFailure(CopyConnectFailure[] failures, bool cancelled)
    {
        if (cancelled)
        {
            return CopyConnectFailure.Cancelled;
        }

        if (failures.Contains(CopyConnectFailure.Refused))
        {
            return CopyConnectFailure.Refused;
        }

        if (failures.Contains(CopyConnectFailure.TimedOut))
        {
            return CopyConnectFailure.TimedOut;
        }

        return CopyConnectFailure.Unreachable;
    }

    private static async Task<CopyConnectFailure> AttemptAsync(
        IPEndPoint endpoint,
        CopyPairingCode pairingCode,
        CopyOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        TaskCompletionSource<CopyChannel> winner,
        CancellationTokenSource winnerStop,
        CancellationToken callerToken)
    {
        Socket? socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        NetworkStream? stream = null;
        CopySession? session = null;
        try
        {
            using (CancellationTokenSource dialing = CancellationTokenSource.CreateLinkedTokenSource(winnerStop.Token))
            {
                dialing.CancelAfter(options.ConnectTimeout);
                try
                {
                    await socket.ConnectAsync(endpoint, dialing.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return callerToken.IsCancellationRequested ? CopyConnectFailure.Cancelled : CopyConnectFailure.TimedOut;
                }
                catch (SocketException)
                {
                    return CopyConnectFailure.Unreachable;
                }
            }

            socket.NoDelay = true;
            stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;

            using CancellationTokenSource handshaking = CancellationTokenSource.CreateLinkedTokenSource(winnerStop.Token);
            handshaking.CancelAfter(options.HandshakeTimeout);

            using CopyInitiator initiator = CopyInitiator.Start(pairingCode);
            await stream.WriteAsync(initiator.FirstMessage.ToArray(), handshaking.Token).ConfigureAwait(false);

            byte[] response = new byte[CopyFrame.HandshakeFrameLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, response, handshaking.Token).ConfigureAwait(false)
                || !initiator.TryFinish(response, out session))
            {
                return CopyConnectFailure.Refused;
            }

            byte[] ready = session.WriteFrame(CopyReady.Instance);
            await stream.WriteAsync(ready, handshaking.Token).ConfigureAwait(false);

            byte[] header = new byte[CopyFrame.HeaderLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, header, handshaking.Token).ConfigureAwait(false)
                || !CopyFrame.TryReadTransportLength(header, out int bodyLength))
            {
                return CopyConnectFailure.Refused;
            }

            byte[] body = new byte[bodyLength];
            if (!await CopyChannel.TryReadExactlyAsync(stream, body, handshaking.Token).ConfigureAwait(false)
                || !session.TryReadFrame(body, out CopyMessage? reply)
                || reply is not CopyReady)
            {
                return CopyConnectFailure.Refused;
            }

            CopyChannel channel = new(stream, session, options, timeProvider, logger);
            stream = null;
            session = null;
            if (winner.TrySetResult(channel))
            {
                await winnerStop.CancelAsync().ConfigureAwait(false);
                return CopyConnectFailure.None;
            }

            await channel.DisposeAsync().ConfigureAwait(false);
            return CopyConnectFailure.Cancelled;
        }
        catch (OperationCanceledException)
        {
            return callerToken.IsCancellationRequested ? CopyConnectFailure.Cancelled : CopyConnectFailure.TimedOut;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            return CopyConnectFailure.Refused;
        }
        finally
        {
            session?.Dispose();
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            socket?.Dispose();
        }
    }
}
