using Microsoft.Extensions.Logging;
using P2PMessenger.Core;
using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Copy;

namespace P2PMessenger.Application.Copy;

public sealed class PcCopyReceiver : IAsyncDisposable
{
    private const int InitialMessageCapacity = 1024;

    private static readonly Action<ILogger, string, Exception?> ReceiveFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(ReceiveFailed)),
        "Saving a copied chat failed with {ExceptionType}.");

    private readonly CopyListener listener;
    private readonly CopiedChatBook copiedChats;
    private readonly CopyOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PcCopyReceiver> logger;
    private readonly IFailureSink? failures;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private CopyChannel? activeChannel;
    private TaskCompletionSource? runCompletion;
    private bool disposed;

    internal PcCopyReceiver(
        CopyListener listener,
        CopiedChatBook copiedChats,
        CopyOptions options,
        TimeProvider timeProvider,
        ILogger<PcCopyReceiver> logger,
        IFailureSink? failures)
    {
        this.listener = listener;
        this.copiedChats = copiedChats;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.failures = failures;
        PairingCodeText = listener.PairingCode.ToText();
    }

    public CopyPairingCode PairingCode => listener.PairingCode;

    public string PairingCodeText { get; }

    public int ListeningPort => listener.LocalEndpoint.Port;

    public async Task<PcCopyReceiveResult> RunAsync(IProgress<PcCopyProgress>? progress, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed)
            {
                return new PcCopyReceiveResult(PcCopyReceivePhase.Cancelled, 0);
            }

            if (runCompletion is not null)
            {
                throw new InvalidOperationException("This PC copy receiver has already been started.");
            }

            runCompletion = completion;
        }

        try
        {
            return await RunCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        CopyChannel? channel;
        Task? running;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            channel = activeChannel;
            running = runCompletion?.Task;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        channel?.Close(ConnectionCloseReason.ClosedLocally);
        await listener.DisposeAsync().ConfigureAwait(false);
        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }

        stop.Dispose();
    }

    private async Task<PcCopyReceiveResult> RunCoreAsync(IProgress<PcCopyProgress>? progress, CancellationToken cancellationToken)
    {
        int saved = 0;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        try
        {
            progress?.Report(new PcCopyProgress(PcCopyReceivePhase.WaitingForPhone, 0, 0));
            CopyChannel? channel = await listener.AcceptAsync(linked.Token).ConfigureAwait(false);
            if (channel is null)
            {
                return new PcCopyReceiveResult(PcCopyReceivePhase.Cancelled, 0);
            }

            lock (gate)
            {
                activeChannel = channel;
            }

            try
            {
                await using (channel.ConfigureAwait(false))
                {
                    progress?.Report(new PcCopyProgress(PcCopyReceivePhase.Connected, 0, 0));
                    CopyMessage? first = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                    if (first is null)
                    {
                        return Ended(saved, linked.Token);
                    }

                    if (first is not CopyTransferStart transferStart)
                    {
                        return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                    }

                    int total = transferStart.ConversationCount;
                    HashSet<CopyOriginId> seenOrigins = [];
                    progress?.Report(new PcCopyProgress(PcCopyReceivePhase.Receiving, 0, total));

                    for (int conversationIndex = 0; conversationIndex < total; conversationIndex++)
                    {
                        CopyMessage? startMessage = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                        if (startMessage is null)
                        {
                            return Ended(saved, linked.Token);
                        }

                        if (startMessage is not CopyConversationStart conversationStart
                            || conversationStart.MessageCount > options.MaxMessagesPerConversation
                            || !seenOrigins.Add(conversationStart.Origin))
                        {
                            return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                        }

                        List<CopiedMessage> messages = new(Math.Min(conversationStart.MessageCount, InitialMessageCapacity));
                        long conversationBytes = 0;
                        for (int messageIndex = 0; messageIndex < conversationStart.MessageCount; messageIndex++)
                        {
                            CopyMessage? item = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                            if (item is null)
                            {
                                return Ended(saved, linked.Token);
                            }

                            if (item is not CopyConversationMessage message || message.Position != messageIndex + 1)
                            {
                                return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                            }

                            conversationBytes += message.ContentBytes;
                            if (conversationBytes > options.MaxConversationBytes)
                            {
                                return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                            }

                            messages.Add(new CopiedMessage(message.Position, message.WrittenByOwner, message.AuthorName, message.Text, message.WrittenAt));
                        }

                        CopyMessage? endMessage = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                        if (endMessage is null)
                        {
                            return Ended(saved, linked.Token);
                        }

                        if (endMessage is not CopyConversationEnd conversationEnd || !conversationEnd.Origin.Equals(conversationStart.Origin))
                        {
                            return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                        }

                        CopiedConversation conversation = new(conversationStart.Origin, conversationStart.Title, timeProvider.GetUtcNow(), messages);
                        await copiedChats.SaveAsync(conversation, linked.Token).ConfigureAwait(false);
                        saved++;
                        progress?.Report(new PcCopyProgress(PcCopyReceivePhase.Receiving, saved, total));

                        if (!await channel.SendAsync(CopyConversationSaved.Create(conversationStart.Origin), linked.Token).ConfigureAwait(false))
                        {
                            return Ended(saved, linked.Token);
                        }
                    }

                    CopyMessage? doneMessage = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                    if (doneMessage is null)
                    {
                        return Ended(saved, linked.Token);
                    }

                    if (doneMessage is not CopyTransferDone transferDone
                        || transferDone.ConversationCount != saved
                        || !CopyTransferComplete.TryCreate(saved, out CopyTransferComplete? complete))
                    {
                        return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
                    }

                    if (!await channel.SendAsync(complete, linked.Token).ConfigureAwait(false))
                    {
                        return Ended(saved, linked.Token);
                    }

                    progress?.Report(new PcCopyProgress(PcCopyReceivePhase.Completed, saved, total));
                    return new PcCopyReceiveResult(PcCopyReceivePhase.Completed, saved);
                }
            }
            finally
            {
                lock (gate)
                {
                    activeChannel = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new PcCopyReceiveResult(PcCopyReceivePhase.Cancelled, saved);
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            if (failures is not null)
            {
                failures.Report(FailureSource.PcCopy, exception);
            }
            else
            {
                ReceiveFailed(logger, Failures.TypeName(exception), null);
            }

            return new PcCopyReceiveResult(PcCopyReceivePhase.Failed, saved);
        }
    }

    private static PcCopyReceiveResult Ended(int saved, CancellationToken token) =>
        new(token.IsCancellationRequested ? PcCopyReceivePhase.Cancelled : PcCopyReceivePhase.Disconnected, saved);
}
