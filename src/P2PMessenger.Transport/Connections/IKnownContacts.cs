using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Transport.Connections;

public interface IKnownContacts
{
    IReadOnlyList<SessionContact> Current { get; }
}
