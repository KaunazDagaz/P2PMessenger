using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Transport.Connections;

internal static class Handshakes
{
    public static async Task<SecureSession?> InitiateAsync(Stream stream, KeySet own, SessionContact contact, CancellationToken cancellationToken)
    {
        using SessionInitiator initiator = SessionInitiator.Start(own, contact);
        await stream.WriteAsync(initiator.FirstFlight.ToArray(), cancellationToken).ConfigureAwait(false);

        byte[] response = new byte[SessionInitiator.ResponseLength];
        await stream.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);
        if (!initiator.TryFinish(response, out SecureSession? session))
        {
            return null;
        }

        try
        {
            byte[] confirmation = session.WriteFrame(KeepAlive.Instance);
            await stream.WriteAsync(confirmation, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public static async Task<ResponderOutcome> RespondAsync(Stream stream, KeySet own, IReadOnlyList<SessionContact> contacts, TimeSpan hintTimeout, CancellationToken cancellationToken)
    {
        byte[] firstFlight = new byte[SessionResponder.FirstFlightLength];
        if (!await TryReadHintAsync(stream, firstFlight.AsMemory(0, ConnectionHint.Length), hintTimeout, cancellationToken).ConfigureAwait(false))
        {
            return ResponderOutcome.Refused(HandshakeRefusal.HintTimedOut);
        }

        if (!ConnectionHint.TryIdentify(firstFlight.AsSpan(0, ConnectionHint.Length), contacts, out SessionContact? caller))
        {
            return ResponderOutcome.Refused(HandshakeRefusal.Stranger);
        }

        await stream.ReadExactlyAsync(firstFlight.AsMemory(ConnectionHint.Length), cancellationToken).ConfigureAwait(false);
        if (!SessionResponder.TryAccept(own, caller, firstFlight, out SecureSession? session, out byte[]? response))
        {
            return ResponderOutcome.Refused(HandshakeRefusal.HandshakeFailed);
        }

        try
        {
            await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            SessionMessage? first = await ReadFirstMessageAsync(stream, session, cancellationToken).ConfigureAwait(false);
            if (first is null)
            {
                session.Dispose();
                return ResponderOutcome.Refused(HandshakeRefusal.NotConfirmed);
            }

            return ResponderOutcome.Accepted(session, first);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static async Task<bool> TryReadHintAsync(Stream stream, Memory<byte> hint, TimeSpan hintTimeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waiting.CancelAfter(hintTimeout);
        try
        {
            await stream.ReadExactlyAsync(hint, waiting.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<SessionMessage?> ReadFirstMessageAsync(Stream stream, SecureSession session, CancellationToken cancellationToken)
    {
        byte[] header = new byte[Frame.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!Frame.TryReadTransportLength(header, out int length))
        {
            return null;
        }

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return session.TryReadFrame(body, out SessionMessage? message) ? message : null;
    }
}
