using P2PMessenger.Application.Copy;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Tests.Copy;

public sealed class CopiedChatBookTests
{
    [Fact]
    [Trait("Story", "US-22")]
    public async Task SaveReplaceAndDelete_OrdersByTitleAndRaisesChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryCopiedChatStore();
        using var owner = KeySet.Create();
        using var bob = KeySet.Create();
        using var alice = KeySet.Create();
        var bobOrigin = CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey);
        var aliceOrigin = CopyOriginId.ForContact(owner.IdentityKey, alice.IdentityKey);

        Assert.True(DisplayName.TryCreate("Bob", out var bobName));
        Assert.True(DisplayName.TryCreate("Alice", out var aliceName));
        var now = DateTimeOffset.UnixEpoch.AddDays(20_000);

        using var book = new CopiedChatBook(store, []);
        var events = new List<CopiedChatChangedEventArgs>();
        book.Changed += (_, args) => events.Add(args);

        await book.SaveAsync(new CopiedConversation(bobOrigin, bobName, now, [new CopiedMessage(1, true, null, "One", now)]), cancellationToken);
        await book.SaveAsync(new CopiedConversation(aliceOrigin, aliceName, now, []), cancellationToken);
        Assert.Equal(["Alice", "Bob"], book.All.Select(static item => item.Title.Value));

        await book.SaveAsync(
            new CopiedConversation(
                bobOrigin,
                bobName,
                now.AddMinutes(5),
                [
                    new CopiedMessage(1, true, null, "One", now),
                    new CopiedMessage(2, false, bobName, "Two", now.AddMinutes(1)),
                ]),
            cancellationToken);
        Assert.True(book.TryGet(bobOrigin, out var updated));
        Assert.Equal(2, updated.Messages.Count);

        Assert.True(await book.DeleteAsync(aliceOrigin, cancellationToken));
        Assert.False(await book.DeleteAsync(aliceOrigin, cancellationToken));
        Assert.Single(book.All);
        Assert.Equal(4, events.Count);
        Assert.Equal(CopiedChatChange.Deleted, events[^1].Change);
        Assert.Null(events[^1].Conversation);
    }
}
