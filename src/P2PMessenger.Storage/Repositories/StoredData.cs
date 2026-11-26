using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class StoredData : IDisposable
{
    private readonly RecordKeys keys;

    internal StoredData(SqliteGate gate, RecordKeys keys)
    {
        this.keys = keys;
        Identity = new IdentityRepository(gate, keys);
        Contacts = new ContactRepository(gate, keys);
        Messages = new MessageRepository(gate, keys);
        SeenMessages = new SeenMessageRepository(gate, keys);
        Groups = new GroupRepository(gate, keys);
        GroupMessages = new GroupMessageRepository(gate, keys);
        CopiedConversations = new CopiedConversationRepository(gate, keys);
    }

    public IdentityRepository Identity { get; }

    public ContactRepository Contacts { get; }

    public MessageRepository Messages { get; }

    public SeenMessageRepository SeenMessages { get; }

    public GroupRepository Groups { get; }

    public GroupMessageRepository GroupMessages { get; }

    public CopiedConversationRepository CopiedConversations { get; }

    public void Dispose() => keys.Dispose();
}
