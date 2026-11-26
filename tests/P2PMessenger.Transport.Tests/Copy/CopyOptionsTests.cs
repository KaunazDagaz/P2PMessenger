using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Copy;

namespace P2PMessenger.Transport.Tests.Copy;

public sealed class CopyOptionsTests
{
    [Fact]
    [Trait("Story", "US-22")]
    public void DefaultOptions_AreValid()
    {
        Assert.Null(new CopyOptions().FindProblem());
    }

    [Fact]
    [Trait("Story", "US-22")]
    public void InvalidTimeoutsOrKeepAliveOrdering_ReportAProblem()
    {
        Assert.NotNull((new CopyOptions { ConnectTimeout = TimeSpan.Zero }).FindProblem());
        Assert.NotNull((new CopyOptions { HandshakeTimeout = TimeSpan.Zero }).FindProblem());
        Assert.NotNull((new CopyOptions { KeepAliveInterval = TimeSpan.Zero }).FindProblem());
        Assert.NotNull((new CopyOptions { IdleTimeout = TimeSpan.FromSeconds(5), KeepAliveInterval = TimeSpan.FromSeconds(5) }).FindProblem());
        Assert.NotNull((new CopyOptions { WriteTimeout = TimeSpan.Zero }).FindProblem());
        Assert.NotNull((new CopyOptions { MaxMessagesPerConversation = 0 }).FindProblem());
        Assert.NotNull((new CopyOptions { MaxMessagesPerConversation = CopyConversationStart.MaxMessages + 1 }).FindProblem());
        Assert.NotNull((new CopyOptions { MaxConversationBytes = 0 }).FindProblem());
        Assert.NotNull((new CopyOptions { MaxConversationBytes = CopyOptions.MaxAllowedConversationBytes + 1 }).FindProblem());
    }
}
