using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Application.Delivery;

internal sealed class Outbox : IAsyncDisposable
{
    private static readonly Action<ILogger, string, Exception?> DeliveryFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(DeliveryFailed)),
        "Sending waiting messages failed with {ExceptionType}; they stay waiting.");

    private readonly ChatLog chats;
    private readonly ConnectionService connections;
    private readonly DeliveryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly IFailureSink? failureSink;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, Courier> couriers = [];
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationToken stopToken;
    private readonly IDisposable wakeUps;
    private bool disposed;

    public Outbox(
        ChatLog chats,
        ConnectionService connections,
        IWakeUpScheduler scheduler,
        DeliveryOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        IFailureSink? failures = null)
    {
        this.chats = chats;
        this.connections = connections;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        failureSink = failures;
        stopToken = stopping.Token;
        connections.Changed += OnConnectionChanged;
        wakeUps = scheduler.Every(options.WakeUpInterval, WakeUp);
    }

    public void Request(Ed25519PublicKey contactKey)
    {
        Courier? courier;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (!couriers.TryGetValue(contactKey, out courier))
            {
                courier = new Courier(this, contactKey);
                couriers.Add(contactKey, courier);
            }
        }

        courier.Request();
    }

    public void WakeUp()
    {
        foreach (Ed25519PublicKey contactKey in chats.ContactsWithWaitingMessages())
        {
            if (connections.IsConnected(contactKey))
            {
                Request(contactKey);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Courier[] stopped;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopped = [.. couriers.Values];
            couriers.Clear();
        }

        connections.Changed -= OnConnectionChanged;
        wakeUps.Dispose();
        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(stopped.Select(static courier => courier.Stop())).ConfigureAwait(false);
        stopping.Dispose();
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (e.ContactKey is { } contactKey && connections.IsConnected(contactKey))
        {
            Request(contactKey);
        }
    }

    private readonly record struct Attempt(ContactConnection Connection, long SentAt);

    private sealed class Courier
    {
        private readonly Outbox outbox;
        private readonly Ed25519PublicKey contactKey;
        private readonly Lock gate = new();
        private readonly Dictionary<MessageId, Attempt> attempts = [];
        private Task loop = Task.CompletedTask;
        private bool running;
        private bool again;
        private bool stopped;

        public Courier(Outbox outbox, Ed25519PublicKey contactKey)
        {
            this.outbox = outbox;
            this.contactKey = contactKey;
        }

        public void Request()
        {
            lock (gate)
            {
                if (stopped)
                {
                    return;
                }

                if (running)
                {
                    again = true;
                    return;
                }

                running = true;
                loop = Task.Run(RunAsync, CancellationToken.None);
            }
        }

        public Task Stop()
        {
            lock (gate)
            {
                stopped = true;
                return loop;
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                await DeliverAsync().ConfigureAwait(false);
                lock (gate)
                {
                    if (!again || stopped)
                    {
                        running = false;
                        return;
                    }

                    again = false;
                }
            }
        }

        private async Task DeliverAsync()
        {
            CancellationToken stop = outbox.stopToken;
            try
            {
                await DeliverWaitingAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (Failures.IsRecoverable(exception))
            {
                if (outbox.failureSink is { } sink)
                {
                    sink.Report(FailureSource.Delivery, exception);
                    return;
                }

                string exceptionType = Failures.TypeName(exception);
                DeliveryFailed(outbox.logger, exceptionType, null);
            }
        }

        private async Task DeliverWaitingAsync(CancellationToken stop)
        {
            if (!outbox.connections.TryGetConnection(contactKey, out ContactConnection? connection))
            {
                return;
            }

            IReadOnlyList<ChatMessage> waiting = outbox.chats.WaitingFor(contactKey);
            HashSet<MessageId> stillWaiting = [.. waiting.Select(static message => message.Id)];
            foreach (MessageId done in attempts.Keys.Where(id => !stillWaiting.Contains(id)).ToList())
            {
                attempts.Remove(done);
            }

            foreach (ChatMessage message in waiting)
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (attempts.TryGetValue(message.Id, out Attempt attempt)
                    && ReferenceEquals(attempt.Connection, connection)
                    && outbox.timeProvider.GetElapsedTime(attempt.SentAt) < outbox.options.ResendAfter)
                {
                    continue;
                }

                if (!message.TryRebuild(out TextMessage? text))
                {
                    continue;
                }

                using (await outbox.chats.EnterDeliveryAsync(contactKey, stop).ConfigureAwait(false))
                {
                    if (!outbox.chats.IsWaiting(contactKey, message.Id))
                    {
                        continue;
                    }

                    if (!await connection.SendAsync(text, stop).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                attempts[message.Id] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
            }
        }
    }
}
