using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Tests.Sessions;

public sealed class ConnectionHintTests
{
    [Fact]
    [Trait("Story", "US-05")]
    public void TryIdentify_EachOfManyContacts_FindsThatContact()
    {
        using var bob = KeySet.Create();
        var callers = Enumerable.Range(0, 12).Select(_ => KeySet.Create()).ToList();
        var contactsOfBob = callers.Select(caller => SessionPeers.Contact(bob, caller)).ToList();
        var bobForCallers = callers.Select(caller => SessionPeers.Contact(caller, bob)).ToList();
        try
        {
            for (var index = 0; index < callers.Count; index++)
            {
                using var initiator = SessionInitiator.Start(callers[index], bobForCallers[index]);

                Assert.True(ConnectionHint.TryIdentify(initiator.FirstFlight[..ConnectionHint.Length], contactsOfBob, out var identified));

                Assert.Same(contactsOfBob[index], identified);
            }
        }
        finally
        {
            bobForCallers.ForEach(contact => contact.Dispose());
            contactsOfBob.ForEach(contact => contact.Dispose());
            callers.ForEach(caller => caller.Dispose());
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void TryIdentify_WithoutContacts_FindsNobody()
    {
        Assert.False(ConnectionHint.TryIdentify(Convert.FromHexString(SessionVectors.Hint), [], out var caller));

        Assert.Null(caller);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public void TryIdentify_RandomBytes_FindNobody()
    {
        using var peers = SessionPeers.Create();
        var hint = new byte[ConnectionHint.Length];

        for (var attempt = 0; attempt < 2000; attempt++)
        {
            RandomNumberGenerator.Fill(hint);

            Assert.False(ConnectionHint.TryIdentify(hint, [peers.AliceForBob, peers.BobForAlice], out _));
        }
    }
}
