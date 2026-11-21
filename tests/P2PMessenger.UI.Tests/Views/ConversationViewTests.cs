using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class ConversationViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public async Task Conversation_ContactWaitingForTheFirstConnection_SaysSoAndAsksToCompareKeys()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);

        Assert.True(UiHarness.Find<Border>(view, "WaitingPanel").IsVisible);
        Assert.StartsWith("Waiting for the first connection with Bob.", UiHarness.Find<TextBlock>(view, "WaitingText").Text, StringComparison.Ordinal);
        Assert.True(UiHarness.Find<Border>(view, "KeyNotComparedPanel").IsVisible);
        Assert.StartsWith("You have not compared keys with Bob.", UiHarness.Find<TextBlock>(view, "KeyNotComparedText").Text, StringComparison.Ordinal);
        Assert.Equal(UiText.ContactWaiting, UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.Equal(contact.Fingerprint.ToGroupedString(), UiHarness.Find<TextBlock>(view, "FingerprintText").Text);
        Assert.True(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);

        page.Draft = "hello";
        await page.SendCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal("Not sent: Bob becomes a contact only after the first connection.", UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Equal("hello", page.Draft);
        Assert.Empty(page.Messages);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task Conversation_Compare_OpensTheFingerprintsAndHidesTheRequestOnceChecked()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);

        page.CompareCommand.Execute(null);
        UiHarness.Flush();
        var verify = Assert.IsType<VerifyContactViewModel>(harness.Shell.CurrentPage);
        await verify.MatchCommand.ExecuteAsync(null);
        harness.Shell.GoBack();
        UiHarness.Flush();

        Assert.Same(page, harness.Shell.CurrentPage);
        Assert.False(UiHarness.Find<Border>(view, "KeyNotComparedPanel").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task Conversation_ConnectedContact_SendsAndShowsMessagesWithNameAndTimeWritten()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var contact = harness.ContactOf(bob);
        var page = new ConversationViewModel(harness.Messenger, contact.IdentityKey, harness.Services.UiThread, harness.Shell, harness.Shell.Pages, TimeZoneInfo.Utc);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        Assert.Equal(UiText.ContactConnected, UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.False(UiHarness.Find<Border>(view, "WaitingPanel").IsVisible);

        page.Draft = "Hi Bob\r\n";
        await page.SendCommand.ExecuteAsync(null);
        var aliceKey = harness.Messenger.Identity.Keys.IdentityKey;
        var reply = await bob.SendAsync(aliceKey, "Hi Alice", CancellationToken.None);
        Assert.Equal(SendOutcome.Queued, reply.Outcome);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 2);
        await UiHarness.WaitUntilAsync(() => page.Messages[0].StateText == UiText.DeliveryReceived);

        Assert.Equal(string.Empty, page.Draft);
        Assert.False(UiHarness.Find<TextBlock>(view, "ErrorText").IsVisible);
        Assert.Equal(["Hi Bob", "Hi Alice"], page.Messages.Select(static message => message.Text));
        Assert.Equal(["Alice", "Bob"], page.Messages.Select(static message => message.Sender));
        Assert.Equal([true, false], page.Messages.Select(static message => message.IsOwn));
        Assert.Null(page.Messages[1].StateText);
        var received = harness.Messenger.Chats.MessagesWith(contact.IdentityKey)[1];
        Assert.Equal(received.WrittenAt.UtcDateTime.ToString("g", CultureInfo.CurrentCulture), page.Messages[1].WrittenAt);
        var texts = UiHarness.Texts(view);
        Assert.Contains("Hi Bob", texts);
        Assert.Contains("Hi Alice", texts);
        Assert.Equal(0, harness.Messenger.Chats.UnreadCount(contact.IdentityKey));
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public async Task Conversation_ContactNotConnected_ShowsTheMessageAsWaiting()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var contact = harness.ContactOf(bob);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => !harness.Messenger.Connections.IsConnected(contact.IdentityKey));
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);

        page.Draft = "are you there?";
        await page.SendCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.ContactNotConnected, UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.False(UiHarness.Find<TextBlock>(view, "ErrorText").IsVisible);
        Assert.Equal(string.Empty, page.Draft);
        var message = Assert.Single(page.Messages);
        Assert.Equal("are you there?", message.Text);
        Assert.Equal(UiText.DeliveryWaiting, message.StateText);
        Assert.Contains(UiText.DeliveryWaiting, UiHarness.Texts(view));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-17")]
    public async Task Conversation_TappingAState_ExplainsInOneLineWhatItConfirms()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var contact = harness.ContactOf(bob);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.WaitUntilAsync(() => !harness.Messenger.Connections.IsConnected(contact.IdentityKey));
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        page.Draft = "hello";
        await page.SendCommand.ExecuteAsync(null);
        UiHarness.Flush();
        var message = Assert.Single(page.Messages);
        Assert.False(message.IsExplanationShown);
        Assert.DoesNotContain(UiText.DeliveryWaitingExplanation, UiHarness.Texts(view));

        message.ToggleExplanationCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(message.IsExplanationShown);
        Assert.Contains(UiText.DeliveryWaitingExplanation, UiHarness.Texts(view));
        Assert.DoesNotContain("\n", UiText.DeliveryWaitingExplanation, StringComparison.Ordinal);

        var sent = harness.Messenger.Chats.MessagesWith(contact.IdentityKey).Single();
        Assert.True(await harness.Messenger.Chats.MarkReceivedAsync(contact.IdentityKey, sent.Id, CancellationToken.None));
        await UiHarness.WaitUntilAsync(() => message.StateText == UiText.DeliveryReceived);

        Assert.Same(message, Assert.Single(page.Messages));
        Assert.Equal(UiText.DeliveryReceivedExplanation, message.StateExplanation);
        Assert.Contains(UiText.DeliveryReceivedExplanation, UiHarness.Texts(view));
        Assert.Contains("does not show whether they have read it", UiText.DeliveryReceivedExplanation, StringComparison.Ordinal);
        message.ToggleExplanationCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(message.IsExplanationShown);
        Assert.DoesNotContain(UiText.DeliveryReceivedExplanation, UiHarness.Texts(view));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task Conversation_LateTexts_AreShownInTheOrderEachSenderWroteThem()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        harness.Shell.Push(page);
        harness.Show(new ConversationView { DataContext = page });

        var first = Text(1, "first");
        await harness.Messenger.Chats.ReceiveAsync(bob.IdentityKey, Text(2, "second"), CancellationToken.None);
        await harness.Messenger.Chats.ReceiveAsync(bob.IdentityKey, first, CancellationToken.None);
        await harness.Messenger.Chats.ReceiveAsync(bob.IdentityKey, first, CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 2);
        await Task.Delay(100);
        UiHarness.Flush();

        Assert.Equal(2, page.Messages.Count);
        Assert.Equal(["first", "second"], page.Messages.Select(static message => message.Text));
        Assert.All(page.Messages, static message => Assert.Equal("Bob", message.Sender));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task Conversation_FirstConnectionRefused_ListsEveryPossibleReason()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await UiHarness.StartPeerAsync("Bob");
        var result = await harness.Messenger.Contacts.AddAsync(harness.Messenger.Contacts.Check(bob.Identity.CodeText), CancellationToken.None);
        Assert.True(result.IsSaved);
        var page = harness.Shell.Pages.Conversation(result.Contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);

        harness.Messenger.Connections.SetAddress(result.Contact.IdentityKey, UiHarness.EndpointOf(bob));
        await UiHarness.WaitUntilAsync(() => page.AttemptText == UiText.DialRefused);

        UiHarness.Flush();
        Assert.True(UiHarness.Find<Border>(view, "AttemptPanel").IsVisible);
        var attempt = UiHarness.Find<TextBlock>(view, "AttemptText");
        Assert.Contains("they have not added your code yet", attempt.Text, StringComparison.Ordinal);
        Assert.Contains("the codes do not match", attempt.Text, StringComparison.Ordinal);
        Assert.Contains("another program or another device answers at this address", attempt.Text, StringComparison.Ordinal);
        Assert.Contains("too many arrived at once", attempt.Text, StringComparison.Ordinal);
        Assert.True(UiHarness.Find<Border>(view, "WaitingPanel").IsVisible);
        Assert.Empty(bob.Contacts.All);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task Conversation_NobodyListening_ListsTheReasonsForNoAnswer()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        using var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        closed.Start();
        var address = (System.Net.IPEndPoint)closed.LocalEndpoint;
        closed.Stop();

        harness.Messenger.Connections.SetAddress(bob.IdentityKey, address);
        await UiHarness.WaitUntilAsync(() => page.AttemptText == UiText.DialUnreachable);

        UiHarness.Flush();
        Assert.True(UiHarness.Find<Border>(view, "AttemptPanel").IsVisible);
        Assert.StartsWith("No answer at this address. Possible reasons:", UiHarness.Find<TextBlock>(view, "AttemptText").Text, StringComparison.Ordinal);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Conversation_ContactWithoutAnAddress_ShowsNoAttempt()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Show(view);

        Assert.Null(page.AttemptText);
        Assert.False(UiHarness.Find<Border>(view, "AttemptPanel").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    private static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddDays(20000).AddMinutes(sequence), text, out var message));
        return message;
    }
}
