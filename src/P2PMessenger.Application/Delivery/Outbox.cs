using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
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
    private readonly GroupBook groups;
    private readonly GroupChats groupChats;
    private readonly GroupPassOn groupPassOn;
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
        GroupBook groups,
        GroupChats groupChats,
        GroupPassOn groupPassOn,
        ConnectionService connections,
        IWakeUpScheduler scheduler,
        DeliveryOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        IFailureSink? failures = null)
    {
        this.chats = chats;
        this.groups = groups;
        this.groupChats = groupChats;
        this.groupPassOn = groupPassOn;
        this.connections = connections;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        failureSink = failures;
        stopToken = stopping.Token;
        connections.Changed += OnConnectionChanged;
        groups.DeliveryNeeded += OnDeliveryNeeded;
        groupChats.DeliveryNeeded += OnDeliveryNeeded;
        groupPassOn.DeliveryNeeded += OnDeliveryNeeded;
        wakeUps = scheduler.Every(options.WakeUpInterval, WakeUp);
    }

    public void Request(Ed25519PublicKey contactKey) => Request(contactKey, false);

    public void WakeUp()
    {
        IEnumerable<Ed25519PublicKey> waiting = chats.ContactsWithWaitingMessages()
            .Concat(groups.ContactsWithPendingNotices())
            .Concat(groupChats.MembersWithWaitingMessages())
            .Concat(groupChats.AuthorsWithRelayedAcknowledgements())
            .Concat(groupPassOn.ContactsWithPendingWork())
            .Distinct();
        foreach (Ed25519PublicKey contactKey in waiting)
        {
            if (connections.IsConnected(contactKey))
            {
                Request(contactKey, true);
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
        groups.DeliveryNeeded -= OnDeliveryNeeded;
        groupChats.DeliveryNeeded -= OnDeliveryNeeded;
        groupPassOn.DeliveryNeeded -= OnDeliveryNeeded;
        wakeUps.Dispose();
        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(stopped.Select(static courier => courier.Stop())).ConfigureAwait(false);
        stopping.Dispose();
    }

    private void Request(Ed25519PublicKey contactKey, bool resetPassOnRound)
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

        courier.Request(resetPassOnRound);
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (e.ContactKey is { } contactKey && connections.IsConnected(contactKey))
        {
            Request(contactKey);
        }
    }

    private void OnDeliveryNeeded(object? sender, Ed25519PublicKey contactKey) => Request(contactKey);

    private readonly record struct Attempt(ContactConnection Connection, long SentAt);

    private sealed class Courier
    {
        private readonly Outbox outbox;
        private readonly Ed25519PublicKey contactKey;
        private readonly Lock gate = new();
        private readonly Dictionary<MessageId, Attempt> attempts = [];
        private readonly Dictionary<MessageId, Attempt> passOnAttempts = [];
        private readonly Dictionary<MessageId, Attempt> relayedAckAttempts = [];
        private ContactConnection? roundConnection;
        private int passedInRound;
        private bool resetRound;
        private Task loop = Task.CompletedTask;
        private bool running;
        private bool again;
        private bool stopped;

        public Courier(Outbox outbox, Ed25519PublicKey contactKey)
        {
            this.outbox = outbox;
            this.contactKey = contactKey;
        }

        public void Request(bool resetPassOnRound)
        {
            lock (gate)
            {
                if (stopped)
                {
                    return;
                }

                if (resetPassOnRound)
                {
                    resetRound = true;
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

            if (ConsumeRoundReset() || !ReferenceEquals(roundConnection, connection))
            {
                roundConnection = connection;
                passedInRound = 0;
            }

            PrunePassOnAttempts(connection);
            PruneRelayedAckAttempts(connection);

            IReadOnlyList<ChatMessage> waiting = outbox.chats.WaitingFor(contactKey);
            IReadOnlyList<PendingSend> groupItems = [.. outbox.groups.PendingNoticesFor(contactKey), .. outbox.groupChats.WaitingFor(contactKey)];
            HashSet<MessageId> stillWaiting = [.. waiting.Select(static message => message.Id), .. groupItems.Select(static item => item.Id)];
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

                if (IsRecent(message.Id, connection))
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

            foreach (PendingSend item in groupItems)
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (IsRecent(item.Id, connection))
                {
                    continue;
                }

                using (await outbox.chats.EnterDeliveryAsync(contactKey, stop).ConfigureAwait(false))
                {
                    if (!(outbox.groups.IsNoticePending(contactKey, item.Id) || outbox.groupChats.IsWaitingFor(contactKey, item.Id)))
                    {
                        continue;
                    }

                    if (!await connection.SendAsync(item.Message, stop).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                attempts[item.Id] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
            }

            foreach (GroupSummary summary in outbox.groupPassOn.TakeSummaries(contactKey, connection))
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (!await connection.SendAsync(summary, stop).ConfigureAwait(false))
                {
                    return;
                }
            }

            foreach (GroupAcknowledgement ownAck in outbox.groupPassOn.PendingOwnAcknowledgementsFor(contactKey))
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (IsRelayedAckRecent(ownAck.NoticeId, connection))
                {
                    continue;
                }

                if (!await connection.SendAsync(ownAck, stop).ConfigureAwait(false))
                {
                    return;
                }

                relayedAckAttempts[ownAck.NoticeId] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
            }

            int sentInBatch = 0;
            bool hasMoreAuthorAcks = false;
            foreach (RelayedGroupAcknowledgement relayed in outbox.groupChats.RelayedAcknowledgementsForAuthor(contactKey))
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (IsRelayedAckRecent(relayed.NoticeId, connection))
                {
                    continue;
                }

                if (sentInBatch >= outbox.options.PassOnBatchSize || passedInRound >= outbox.options.PassOnRoundLimit)
                {
                    hasMoreAuthorAcks = true;
                    break;
                }

                using (await outbox.chats.EnterDeliveryAsync(contactKey, stop).ConfigureAwait(false))
                {
                    if (!outbox.groupChats.TryGetRelayedAcknowledgement(relayed.NoticeId, out RelayedGroupAcknowledgement? current))
                    {
                        continue;
                    }

                    if (!await connection.SendAsync(current.Acknowledgement, stop).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                relayedAckAttempts[relayed.NoticeId] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
                sentInBatch++;
                passedInRound++;
            }

            while (sentInBatch < outbox.options.PassOnBatchSize && passedInRound < outbox.options.PassOnRoundLimit)
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (!outbox.groupPassOn.TryDequeueMessage(contactKey, connection, out PassOnItem item))
                {
                    break;
                }

                if (IsPassOnRecent(item.Id, connection))
                {
                    continue;
                }

                using (await outbox.chats.EnterDeliveryAsync(contactKey, stop).ConfigureAwait(false))
                {
                    if (!outbox.groupChats.TryGetStoredEnvelope(item.GroupId, item.Id, out GroupMessage? envelope))
                    {
                        continue;
                    }

                    if (!await connection.SendAsync(envelope, stop).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                passOnAttempts[item.Id] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
                sentInBatch++;
                passedInRound++;
            }

            bool hasMorePassOnAcks = false;
            foreach (MessageId noticeId in outbox.groupPassOn.PendingAcknowledgementsFor(contactKey, connection))
            {
                if (stop.IsCancellationRequested || connection.IsClosed)
                {
                    return;
                }

                if (IsRelayedAckRecent(noticeId, connection))
                {
                    continue;
                }

                if (sentInBatch >= outbox.options.PassOnBatchSize || passedInRound >= outbox.options.PassOnRoundLimit)
                {
                    hasMorePassOnAcks = true;
                    break;
                }

                using (await outbox.chats.EnterDeliveryAsync(contactKey, stop).ConfigureAwait(false))
                {
                    if (!outbox.groupChats.TryGetRelayedAcknowledgement(noticeId, out RelayedGroupAcknowledgement? current))
                    {
                        outbox.groupPassOn.ForgetAcknowledgement(noticeId);
                        continue;
                    }

                    if (!await connection.SendAsync(current.Acknowledgement, stop).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                relayedAckAttempts[noticeId] = new Attempt(connection, outbox.timeProvider.GetTimestamp());
                sentInBatch++;
                passedInRound++;
            }

            if (passedInRound < outbox.options.PassOnRoundLimit && (hasMoreAuthorAcks || hasMorePassOnAcks || outbox.groupPassOn.HasPendingMessages(contactKey, connection)))
            {
                ContinueSoon();
            }
        }

        private bool ConsumeRoundReset()
        {
            lock (gate)
            {
                if (!resetRound)
                {
                    return false;
                }

                resetRound = false;
                return true;
            }
        }

        private void ContinueSoon()
        {
            lock (gate)
            {
                if (!stopped)
                {
                    again = true;
                }
            }
        }

        private void PrunePassOnAttempts(ContactConnection connection)
        {
            foreach (MessageId expired in passOnAttempts
                .Where(pair => !ReferenceEquals(pair.Value.Connection, connection) || outbox.timeProvider.GetElapsedTime(pair.Value.SentAt) >= outbox.options.ResendAfter)
                .Select(static pair => pair.Key)
                .ToList())
            {
                passOnAttempts.Remove(expired);
            }
        }

        private void PruneRelayedAckAttempts(ContactConnection connection)
        {
            foreach (MessageId expired in relayedAckAttempts
                .Where(pair => !ReferenceEquals(pair.Value.Connection, connection) || outbox.timeProvider.GetElapsedTime(pair.Value.SentAt) >= outbox.options.ResendAfter)
                .Select(static pair => pair.Key)
                .ToList())
            {
                relayedAckAttempts.Remove(expired);
            }
        }

        private bool IsRecent(MessageId id, ContactConnection connection) =>
            attempts.TryGetValue(id, out Attempt attempt)
            && ReferenceEquals(attempt.Connection, connection)
            && outbox.timeProvider.GetElapsedTime(attempt.SentAt) < outbox.options.ResendAfter;

        private bool IsPassOnRecent(MessageId id, ContactConnection connection) =>
            passOnAttempts.TryGetValue(id, out Attempt attempt)
            && ReferenceEquals(attempt.Connection, connection)
            && outbox.timeProvider.GetElapsedTime(attempt.SentAt) < outbox.options.ResendAfter;

        private bool IsRelayedAckRecent(MessageId id, ContactConnection connection) =>
            relayedAckAttempts.TryGetValue(id, out Attempt attempt)
            && ReferenceEquals(attempt.Connection, connection)
            && outbox.timeProvider.GetElapsedTime(attempt.SentAt) < outbox.options.ResendAfter;
    }
}
