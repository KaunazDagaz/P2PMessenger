using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Core.Tests.Sessions;

namespace P2PMessenger.Core.Tests.Messages;

public sealed class SeenMessageIdsTests
{
    [Fact]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-09")]
    public async Task ResentMessage_InALaterSession_IsRecognisedAsSeen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seen = new InMemorySeenMessageIds();
        using var peers = SessionPeers.CreateRandom();
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, "sent twice", out var message));

        var first = Deliver(peers, message);
        Assert.False(await seen.ContainsAsync(first.Id, cancellationToken));
        await seen.AddAsync(first.Id, cancellationToken);
        var second = Deliver(peers, message);

        Assert.Equal(first, second);
        Assert.True(await seen.ContainsAsync(second.Id, cancellationToken));
    }

    private static TextMessage Deliver(SessionPeers peers, TextMessage message)
    {
        var (alice, bob) = peers.ConnectConfirmed();
        using (alice)
        using (bob)
        {
            Assert.True(bob.TryReadFrame(alice.WriteFrame(message).AsSpan(Frame.HeaderLength), out var received));
            return Assert.IsType<TextMessage>(received);
        }
    }

    private sealed class InMemorySeenMessageIds : ISeenMessageIds
    {
        private readonly HashSet<MessageId> _ids = [];

        public ValueTask<bool> ContainsAsync(MessageId id, CancellationToken cancellationToken) => ValueTask.FromResult(_ids.Contains(id));

        public ValueTask AddAsync(MessageId id, CancellationToken cancellationToken)
        {
            _ids.Add(id);
            return ValueTask.CompletedTask;
        }
    }
}
