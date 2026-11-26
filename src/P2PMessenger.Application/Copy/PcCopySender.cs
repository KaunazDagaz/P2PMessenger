using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Copy;

namespace P2PMessenger.Application.Copy;

public sealed class PcCopySender : IAsyncDisposable
{
    private readonly CopyChannel channel;
    private readonly Func<IReadOnlyList<CopyOriginId>, IReadOnlyList<CopySnapshot>> buildSnapshots;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private TaskCompletionSource? sendCompletion;
    private bool sent;
    private bool disposed;

    internal PcCopySender(CopyChannel channel, Func<IReadOnlyList<CopyOriginId>, IReadOnlyList<CopySnapshot>> buildSnapshots)
    {
        this.channel = channel;
        this.buildSnapshots = buildSnapshots;
    }

    public async Task<PcCopySendResult> SendAsync(
        IReadOnlyList<CopyOriginId> selectedOrigins,
        IProgress<PcCopySendProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedOrigins);

        IReadOnlyList<CopySnapshot> snapshots = buildSnapshots(selectedOrigins);
        if (snapshots.Count == 0)
        {
            return new PcCopySendResult(PcCopySendOutcome.NothingSelected, 0);
        }

        if (snapshots.Count > CopyTransferStart.MaxConversations)
        {
            return new PcCopySendResult(PcCopySendOutcome.Failed, 0);
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed)
            {
                return new PcCopySendResult(PcCopySendOutcome.Cancelled, 0);
            }

            if (sent)
            {
                throw new InvalidOperationException("This PC copy connection has already been used.");
            }

            sent = true;
            sendCompletion = completion;
        }

        try
        {
            return await SendCoreAsync(snapshots, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            running = sendCompletion?.Task;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        channel.Close(ConnectionCloseReason.ClosedLocally);
        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }

        await channel.DisposeAsync().ConfigureAwait(false);
        stop.Dispose();
    }

    private async Task<PcCopySendResult> SendCoreAsync(
        IReadOnlyList<CopySnapshot> snapshots,
        IProgress<PcCopySendProgress>? progress,
        CancellationToken cancellationToken)
    {
        int copied = 0;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        try
        {
            if (!CopyTransferStart.TryCreate(snapshots.Count, out CopyTransferStart? transferStart))
            {
                return new PcCopySendResult(PcCopySendOutcome.Failed, 0);
            }

            if (!await channel.SendAsync(transferStart, linked.Token).ConfigureAwait(false))
            {
                return Ended(copied, linked.Token);
            }

            progress?.Report(new PcCopySendProgress(0, snapshots.Count));

            foreach (CopySnapshot snapshot in snapshots)
            {
                if (!CopyConversationStart.TryCreate(snapshot.Origin, snapshot.Title, snapshot.Messages.Count, out CopyConversationStart? conversationStart))
                {
                    return new PcCopySendResult(PcCopySendOutcome.Failed, copied);
                }

                if (!await channel.SendAsync(conversationStart, linked.Token).ConfigureAwait(false))
                {
                    return Ended(copied, linked.Token);
                }

                foreach (CopiedMessage message in snapshot.Messages)
                {
                    if (!CopyConversationMessage.TryCreate(
                        message.Position,
                        message.WrittenByOwner,
                        message.AuthorName,
                        message.WrittenAt,
                        message.Text,
                        out CopyConversationMessage? wire))
                    {
                        return new PcCopySendResult(PcCopySendOutcome.Failed, copied);
                    }

                    if (!await channel.SendAsync(wire, linked.Token).ConfigureAwait(false))
                    {
                        return Ended(copied, linked.Token);
                    }
                }

                if (!await channel.SendAsync(CopyConversationEnd.Create(snapshot.Origin), linked.Token).ConfigureAwait(false))
                {
                    return Ended(copied, linked.Token);
                }

                CopyMessage? ack = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                if (ack is null)
                {
                    return Ended(copied, linked.Token);
                }

                if (ack is not CopyConversationSaved saved || !saved.Origin.Equals(snapshot.Origin))
                {
                    return new PcCopySendResult(PcCopySendOutcome.Failed, copied);
                }

                copied++;
                progress?.Report(new PcCopySendProgress(copied, snapshots.Count));
            }

            if (!CopyTransferDone.TryCreate(copied, out CopyTransferDone? transferDone))
            {
                return new PcCopySendResult(PcCopySendOutcome.Failed, copied);
            }

            if (!await channel.SendAsync(transferDone, linked.Token).ConfigureAwait(false))
            {
                return Ended(copied, linked.Token);
            }

            CopyMessage? done = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
            if (done is null)
            {
                return Ended(copied, linked.Token);
            }

            if (done is not CopyTransferComplete complete || complete.SavedCount != copied)
            {
                return new PcCopySendResult(PcCopySendOutcome.Failed, copied);
            }

            return new PcCopySendResult(PcCopySendOutcome.Completed, copied);
        }
        catch (OperationCanceledException)
        {
            return new PcCopySendResult(PcCopySendOutcome.Cancelled, copied);
        }
    }

    private static PcCopySendResult Ended(int copied, CancellationToken token) =>
        new(token.IsCancellationRequested ? PcCopySendOutcome.Cancelled : PcCopySendOutcome.Disconnected, copied);
}
