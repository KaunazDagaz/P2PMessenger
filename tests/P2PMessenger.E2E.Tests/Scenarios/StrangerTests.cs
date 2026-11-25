using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class StrangerTests
{
    [AvaloniaFact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task Stranger_ConnectsToTheListeningPort_IsClosedWithoutAnAnswerAndTheChatKeepsWorking()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob);
        int port = alice.Services.Messenger?.Connections.ListeningPort ?? throw new InvalidOperationException("The app of Alice is not listening.");

        using TcpClient stranger = new();
        await stranger.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
        NetworkStream stream = stranger.GetStream();
        await stream.WriteAsync(RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken);

        Assert.True(await IsClosedWithoutAnswerAsync(stream));
        await Flows.GoBackToContactsAsync(alice);
        Assert.Equal("Bob", Assert.Single(alice.ContactNames()));
        Assert.Null(alice.TryNamed<Avalonia.Controls.Border>("ErrorNoticePanel"));

        await Flows.OpenConversationAsync(alice, "Bob");
        await Flows.SendAsync(alice, "Still working after the stranger.");
        await bob.WaitForTextAsync("Still working after the stranger.");
    }

    [AvaloniaFact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task Stranger_HoldsOnlyTheCodeOfAContact_NeverBecomesAContactOfThatPerson()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await using Device mallory = new(hub, "Mallory");
        await Flows.SetUpPairAsync(alice, bob);
        await Flows.GoBackToContactsAsync(alice);
        string aliceCode = await Flows.ReadOwnCodeAsync(alice);

        await mallory.LaunchAsync();
        await Flows.CreateIdentityAsync(mallory);
        await Flows.AddContactAsync(mallory, aliceCode, "Alice");
        await mallory.WaitForTextAsync(UiText.ContactWaiting);
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal("Bob", Assert.Single(alice.ContactNames()));
        Assert.False(alice.Shows(Flows.AddedNotice("Mallory")));
        Assert.True(mallory.Shows(UiText.ContactWaiting));
        Assert.False(mallory.Shows(UiText.ContactReachableNearby));

        await Flows.OpenConversationAsync(alice, "Bob");
        await Flows.SendAsync(alice, "Mallory cannot read this.");
        await bob.WaitForTextAsync("Mallory cannot read this.");
        Assert.False(mallory.Shows("Mallory cannot read this."));
    }

    private static async Task<bool> IsClosedWithoutAnswerAsync(NetworkStream stream)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] buffer = new byte[64];
        try
        {
            return await stream.ReadAsync(buffer, limit.Token) == 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
