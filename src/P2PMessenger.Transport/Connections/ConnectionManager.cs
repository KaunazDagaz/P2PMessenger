using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Connections;

public sealed class ConnectionManager : IAsyncDisposable
{
    private readonly KeySet own;
    private readonly IKnownContacts contacts;
    private readonly AdmissionControl admission;
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, ContactConnection> current = [];
    private readonly HashSet<ContactConnection> live = [];
    private readonly Dictionary<(Ed25519PublicKey Key, ConnectionRoute Route), Task<ConnectResult>> dials = [];
    private readonly Dictionary<Ed25519PublicKey, Keeper> keepers = [];
    private long lastNumber;
    private bool disposed;

    public ConnectionManager(
        KeySet own,
        IKnownContacts contacts,
        IConnectionObserver observer,
        ConnectionOptions options,
        TimeProvider timeProvider,
        ILogger<ConnectionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        string? problem = options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        this.own = own;
        this.contacts = contacts;
        Observer = observer;
        Options = options;
        TimeProvider = timeProvider;
        Logger = logger;
        admission = new AdmissionControl(options.Admission, timeProvider);
        shutdownToken = shutdown.Token;
    }

    internal IConnectionObserver Observer { get; }

    internal ConnectionOptions Options { get; }

    internal TimeProvider TimeProvider { get; }

    internal ILogger Logger { get; }

    internal CancellationToken ShutdownToken => shutdownToken;

    private bool IsDisposed
    {
        get
        {
            lock (gate)
            {
                return disposed;
            }
        }
    }

    public bool TryGetConnection(Ed25519PublicKey identityKey, [NotNullWhen(true)] out ContactConnection? connection)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        lock (gate)
        {
            if (current.TryGetValue(identityKey, out connection) && !connection.IsClosed)
            {
                return true;
            }

            connection = null;
            return false;
        }
    }

    public Task<bool> SendAsync(Ed25519PublicKey identityKey, SessionMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return TryGetConnection(identityKey, out ContactConnection? connection)
            ? connection.SendAsync(message, cancellationToken)
            : Task.FromResult(false);
    }

    public Task<ConnectResult> ConnectAsync(SessionContact contact, IDialer dialer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(dialer);

        ConnectionRoute route = dialer.Route;
        Task<ConnectResult>? dial;
        TaskCompletionSource<ConnectResult>? started = null;
        lock (gate)
        {
            if (disposed)
            {
                return Task.FromResult(ConnectResult.Failed(ConnectFailure.ShutDown));
            }

            if (current.TryGetValue(contact.IdentityKey, out ContactConnection? existing)
                && !existing.IsClosed
                && (existing.Route == route || existing.Route == ConnectionRoute.Tor))
            {
                return Task.FromResult(ConnectResult.Connected(existing));
            }

            if (!dials.TryGetValue((contact.IdentityKey, route), out dial))
            {
                started = new TaskCompletionSource<ConnectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                dial = started.Task;
                dials.Add((contact.IdentityKey, route), dial);
            }
        }

        if (started is not null)
        {
            _ = DialAsync(contact, dialer, started);
        }

        return dial.WaitAsync(cancellationToken);
    }

    public void KeepConnected(SessionContact contact, IDialer dialer)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(dialer);

        Keeper? previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            keepers.Remove(contact.IdentityKey, out previous);
            CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            CancellationToken stopToken = stop.Token;
            Task loop = Task.Run(() => KeepConnectedLoopAsync(contact, dialer, stopToken), CancellationToken.None);
            keepers.Add(contact.IdentityKey, new Keeper(stop, loop));
        }

        if (previous is not null)
        {
            _ = RetireAsync(previous);
        }
    }

    public bool StopKeepingConnected(Ed25519PublicKey identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        Keeper? keeper;
        lock (gate)
        {
            if (!keepers.Remove(identityKey, out keeper))
            {
                return false;
            }
        }

        _ = RetireAsync(keeper);
        return true;
    }

    public async Task AcceptAsync(Stream stream, IPAddress? source, ConnectionRoute route, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long number = Interlocked.Increment(ref lastNumber);
        if (IsDisposed)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (!admission.TryAdmit(source, out AdmissionSlot? slot, out AdmissionRefusal refusal))
        {
            Logger.NotAdmitted(number, refusal);
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        long started = TimeProvider.GetTimestamp();
        ResponderOutcome outcome;
        try
        {
            outcome = await RespondWithinTimeoutAsync(stream, route, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportUnexpected(number, exception);
            outcome = ResponderOutcome.Refused(HandshakeRefusal.Closed);
        }
        finally
        {
            slot.Dispose();
        }

        if (outcome.Session is null)
        {
            HandshakeRefusal reason = outcome.Refusal;
            Logger.Refused(number, reason);
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        ContactConnection connection = new(number, stream, outcome.Session, outcome.FirstMessage, route, this);
        _ = Adopt(connection, ConnectionDirection.Inbound, ElapsedMilliseconds(started));
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose(out ContactConnection[] connections, out Keeper[] keepers, out Task[] dials))
        {
            return;
        }

        await shutdown.CancelAsync().ConfigureAwait(false);
        foreach (Keeper keeper in keepers)
        {
            keeper.Cancel();
        }

        foreach (ContactConnection connection in connections)
        {
            connection.Close(ConnectionCloseReason.ShutDown);
        }

        await Task.WhenAll(keepers.Select(static keeper => keeper.Loop)).ConfigureAwait(false);
        await Task.WhenAll(dials).ConfigureAwait(false);
        await Task.WhenAll(connections.Select(static connection => connection.Completion)).ConfigureAwait(false);
        foreach (Keeper keeper in keepers)
        {
            keeper.Dispose();
        }

        shutdown.Dispose();
    }

    internal void Detach(ContactConnection connection)
    {
        lock (gate)
        {
            live.Remove(connection);
            Ed25519PublicKey key = connection.Contact.IdentityKey;
            if (current.TryGetValue(key, out ContactConnection? existing) && ReferenceEquals(existing, connection))
            {
                current.Remove(key);
            }
        }
    }

    private static TimeSpan Jittered(TimeSpan delay) => delay * (0.8 + (RandomNumberGenerator.GetInt32(0, 401) / 1000.0));

    private static TimeSpan Doubled(TimeSpan delay, TimeSpan longest)
    {
        TimeSpan doubled = delay * 2;
        return doubled < longest ? doubled : longest;
    }

    private static async Task RetireAsync(Keeper keeper)
    {
        keeper.Cancel();
        await keeper.Loop.ConfigureAwait(false);
        keeper.Dispose();
    }

    private async Task DialAsync(SessionContact contact, IDialer dialer, TaskCompletionSource<ConnectResult> completion)
    {
        long number = Interlocked.Increment(ref lastNumber);
        ConnectResult result;
        try
        {
            result = await DialOnceAsync(number, contact, dialer).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportUnexpected(number, exception);
            result = ConnectResult.Failed(ConnectFailure.Refused);
        }

        if (!result.IsConnected)
        {
            ConnectFailure failure = result.Failure;
            Logger.DialFailed(number, failure);
        }

        ForgetDial(contact.IdentityKey, dialer.Route, completion.Task);
        completion.TrySetResult(result);
    }

    private async Task<ConnectResult> DialOnceAsync(long number, SessionContact contact, IDialer dialer)
    {
        long started = TimeProvider.GetTimestamp();
        ConnectionRoute route = dialer.Route;
        Stream? stream;
        using (CancellationTokenSource dialing = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
        {
            dialing.CancelAfter(Options.DialTimeoutFor(route));
            try
            {
                stream = await dialer.DialAsync(dialing.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ConnectResult.Failed(shutdownToken.IsCancellationRequested ? ConnectFailure.ShutDown : ConnectFailure.TimedOut);
            }
        }

        if (stream is null)
        {
            return ConnectResult.Failed(ConnectFailure.Unreachable);
        }

        SecureSession? session;
        ConnectFailure failure = ConnectFailure.Refused;
        using (CancellationTokenSource handshaking = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
        {
            handshaking.CancelAfter(Options.HandshakeTimeoutFor(route));
            try
            {
                session = await Handshakes.InitiateAsync(stream, own, contact, handshaking.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                session = null;
                failure = shutdownToken.IsCancellationRequested ? ConnectFailure.ShutDown : ConnectFailure.TimedOut;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
            {
                session = null;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        if (session is null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return ConnectResult.Failed(failure);
        }

        ContactConnection connection = new(number, stream, session, null, route, this);
        ContactConnection? kept = Adopt(connection, ConnectionDirection.Outbound, ElapsedMilliseconds(started));
        return kept is null ? ConnectResult.Failed(ConnectFailure.ShutDown) : ConnectResult.Connected(kept);
    }

    private async Task<ResponderOutcome> RespondWithinTimeoutAsync(Stream stream, ConnectionRoute route, CancellationToken cancellationToken)
    {
        using CancellationTokenSource handshaking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken);
        handshaking.CancelAfter(Options.HandshakeTimeoutFor(route));
        try
        {
            return await Handshakes.RespondAsync(stream, own, contacts.Current, Options.HintTimeoutFor(route), handshaking.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ResponderOutcome.Refused(HandshakeRefusal.TimedOut);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
        {
            return ResponderOutcome.Refused(HandshakeRefusal.Closed);
        }
    }

    private async Task KeepConnectedLoopAsync(SessionContact contact, IDialer dialer, CancellationToken stop)
    {
        TimeSpan delay = Options.ReconnectDelayMin;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (TryGetConnection(contact.IdentityKey, out ContactConnection? current))
                {
                    await current.Completion.WaitAsync(stop).ConfigureAwait(false);
                    delay = Options.ReconnectDelayMin;
                    await Task.Delay(Jittered(delay), TimeProvider, stop).ConfigureAwait(false);
                    continue;
                }

                ConnectResult result = await ConnectAsync(contact, dialer, stop).ConfigureAwait(false);
                if (result.IsConnected)
                {
                    delay = Options.ReconnectDelayMin;
                    continue;
                }

                if (result.Failure == ConnectFailure.ShutDown)
                {
                    return;
                }

                await Task.Delay(Jittered(delay), TimeProvider, stop).ConfigureAwait(false);
                delay = Doubled(delay, Options.ReconnectDelayMax);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private ContactConnection? Adopt(ContactConnection candidate, ConnectionDirection direction, long milliseconds)
    {
        long number = candidate.Number;
        if (TryRegister(candidate, out ContactConnection? kept, out ContactConnection? replaced))
        {
            ConnectionRoute route = candidate.Route;
            Logger.Established(number, direction, route, milliseconds);
            candidate.Start();
            replaced?.Close(ConnectionCloseReason.Replaced);
            return candidate;
        }

        if (kept is null)
        {
            candidate.Abandon(ConnectionCloseReason.ShutDown);
            return null;
        }

        Logger.NotKept(number);
        candidate.Abandon(ConnectionCloseReason.Replaced);
        return kept;
    }

    private bool TryRegister(ContactConnection candidate, out ContactConnection? kept, out ContactConnection? replaced)
    {
        replaced = null;
        lock (gate)
        {
            if (disposed)
            {
                kept = null;
                return false;
            }

            Ed25519PublicKey key = candidate.Contact.IdentityKey;
            if (current.TryGetValue(key, out ContactConnection? existing) && !existing.IsClosed)
            {
                if (!Prefers(candidate, existing))
                {
                    kept = existing;
                    return false;
                }

                replaced = existing;
            }

            current[key] = candidate;
            live.Add(candidate);
            kept = candidate;
            return true;
        }
    }

    private bool Prefers(ContactConnection candidate, ContactConnection existing)
    {
        bool ownStaticKeyIsLower = own.StaticKey.Bytes.SequenceCompareTo(candidate.Contact.StaticKey.Bytes) < 0;
        TimeSpan existingAge = TimeProvider.GetElapsedTime(existing.EstablishedAt);
        return SessionPreference.NewReplacesExisting(
            existing.Route,
            existing.IsInitiator,
            existingAge,
            candidate.Route,
            candidate.IsInitiator,
            ownStaticKeyIsLower,
            Options.SimultaneousOpenWindow);
    }

    private void ForgetDial(Ed25519PublicKey identityKey, ConnectionRoute route, Task<ConnectResult> dial)
    {
        lock (gate)
        {
            if (dials.TryGetValue((identityKey, route), out Task<ConnectResult>? current) && ReferenceEquals(current, dial))
            {
                dials.Remove((identityKey, route));
            }
        }
    }

    private bool TryBeginDispose(out ContactConnection[] connections, out Keeper[] keeperValues, out Task[] dialTasks)
    {
        lock (gate)
        {
            if (disposed)
            {
                connections = [];
                keeperValues = [];
                dialTasks = [];
                return false;
            }

            disposed = true;
            connections = [.. live];
            keeperValues = [.. keepers.Values];
            dialTasks = [.. dials.Values];
            keepers.Clear();
            return true;
        }
    }

    private long ElapsedMilliseconds(long started) => (long)TimeProvider.GetElapsedTime(started).TotalMilliseconds;

    private void ReportUnexpected(long number, Exception exception)
    {
        string exceptionType = exception.GetType().Name;
        Logger.Unexpected(number, exceptionType);
    }

    private sealed class Keeper : IDisposable
    {
        private readonly CancellationTokenSource stop;

        public Keeper(CancellationTokenSource stop, Task loop)
        {
            this.stop = stop;
            Loop = loop;
        }

        public Task Loop { get; }

        public void Cancel() => stop.Cancel();

        public void Dispose() => stop.Dispose();
    }
}
