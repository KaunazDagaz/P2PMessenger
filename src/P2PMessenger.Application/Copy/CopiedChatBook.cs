using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Copy;

namespace P2PMessenger.Application.Copy;

public sealed class CopiedChatBook : IDisposable
{
    private readonly ICopiedChatStore store;
    private readonly SemaphoreSlim writing = new(1, 1);
    private CopiedConversation[] conversations;

    internal CopiedChatBook(ICopiedChatStore store, IEnumerable<CopiedConversation> loaded)
    {
        this.store = store;
        conversations = Ordered(loaded);
    }

    public event EventHandler<CopiedChatChangedEventArgs>? Changed;

    public IReadOnlyList<CopiedConversation> All => Volatile.Read(ref conversations);

    public bool TryGet(CopyOriginId origin, [NotNullWhen(true)] out CopiedConversation? conversation)
    {
        ArgumentNullException.ThrowIfNull(origin);

        conversation = Array.Find(Volatile.Read(ref conversations), item => item.Origin.Equals(origin));
        return conversation is not null;
    }

    public async Task SaveAsync(CopiedConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
            CopiedConversation[] current = conversations;
            List<CopiedConversation> next = new(current.Length + 1);
            bool replaced = false;
            foreach (CopiedConversation item in current)
            {
                if (item.Origin.Equals(conversation.Origin))
                {
                    next.Add(conversation);
                    replaced = true;
                }
                else
                {
                    next.Add(item);
                }
            }

            if (!replaced)
            {
                next.Add(conversation);
            }

            Volatile.Write(ref conversations, Ordered(next));
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new CopiedChatChangedEventArgs(conversation.Origin, CopiedChatChange.Saved, conversation));
    }

    public async Task<bool> DeleteAsync(CopyOriginId origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        bool deleted;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            deleted = await store.DeleteAsync(origin, cancellationToken).ConfigureAwait(false);
            if (!deleted)
            {
                return false;
            }

            CopiedConversation[] current = conversations;
            Volatile.Write(ref conversations, Ordered(current.Where(item => !item.Origin.Equals(origin))));
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new CopiedChatChangedEventArgs(origin, CopiedChatChange.Deleted, null));
        return true;
    }

    public void Dispose() => writing.Dispose();

    private static CopiedConversation[] Ordered(IEnumerable<CopiedConversation> unordered) =>
        [.. unordered
            .OrderBy(static item => item.Title.Value, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static item => Convert.ToHexString(item.Origin.Bytes), StringComparer.Ordinal)];
}
