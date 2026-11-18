using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class AddContactViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-03")]
    public async Task AddContact_PastedTextCode_ShowsNameAndFingerprintAndSavesAWaitingContact()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        using var bob = KeySet.Create();
        var code = Code(bob, "Bob");

        page.CodeText = "  " + code.ToText() + "\n";
        page.CheckCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(UiHarness.Find<Border>(view, "PreviewPanel").IsVisible);
        Assert.Equal("Bob", UiHarness.Find<TextBlock>(view, "PreviewName").Text);
        Assert.Equal(code.Fingerprint.ToGroupedString(), UiHarness.Find<TextBlock>(view, "PreviewFingerprint").Text);
        Assert.Equal(UiText.AddContactNewHint, UiHarness.Find<TextBlock>(view, "PreviewHint").Text);
        Assert.Equal(UiText.AddContactAdd, UiHarness.Find<Button>(view, "AddButton").Content);
        Assert.False(UiHarness.Find<TextBlock>(view, "ErrorText").IsVisible);

        await page.AddCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var contact));
        Assert.Equal(ContactState.WaitingForFirstConnection, contact.State);
        Assert.Equal(KeyComparison.NotCompared, contact.KeyComparison);
        var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        Assert.True(conversation.IsWaiting);
        Assert.True(conversation.IsKeyNotCompared);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaTheory]
    [Trait("Story", "US-02")]
    [InlineData("hello", "This is not a P2PMessenger contact code.")]
    [InlineData("P2PM2-AAAA", "This code comes from another version of the app and cannot be read.")]
    [InlineData("P2PM1-AEAAAAAA", "This code is damaged. Ask for it again.")]
    public async Task AddContact_UnreadableCode_ShowsWhyAndAddsNothing(string text, string expected)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);

        page.CodeText = text;
        page.CheckCommand.Execute(null);
        UiHarness.Flush();

        Assert.Equal(expected, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(UiHarness.Find<Border>(view, "PreviewPanel").IsVisible);
        Assert.False(page.AddCommand.CanExecute(null));
        Assert.Empty(harness.Messenger.Contacts.All);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task AddContact_OwnCode_IsRefused()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);

        page.CodeText = harness.Messenger.Identity.CodeText;
        page.CheckCommand.Execute(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactOwnCode, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(page.HasPreview);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-05")]
    public async Task AddContact_NewIdentityUnderASavedName_WarnsAndOffersToAddItAsANewContact()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var oldBob = KeySet.Create();
        using var newBob = KeySet.Create();
        await harness.AddContactAsync(oldBob, "Bob");
        var (page, view) = Open(harness);

        page.CodeText = Code(newBob, "Bob").ToText();
        page.CheckCommand.Execute(null);
        UiHarness.Flush();

        Assert.StartsWith("A saved contact is called Bob, but this code has a different key.", UiHarness.Find<TextBlock>(view, "PreviewHint").Text, StringComparison.Ordinal);
        Assert.Equal(UiText.AddContactAddAsNew, UiHarness.Find<Button>(view, "AddButton").Content);
        await page.AddCommand.ExecuteAsync(null);
        Assert.Equal(2, harness.Messenger.Contacts.All.Count);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-05")]
    public async Task AddContact_SameCodeAgain_SaysItIsAlreadyAContact()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        var (page, view) = Open(harness);

        page.CodeText = Code(bob, "Bob").ToText();
        page.CheckCommand.Execute(null);
        UiHarness.Flush();

        Assert.Equal("Bob is already a contact.", UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(page.HasPreview);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-03")]
    public async Task AddContact_CodeFile_IsReadAndChecked()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        using var bob = KeySet.Create();
        harness.Platform.OpenResult = new CodeFileResult(CodeFileStatus.Done, Code(bob, "Bob").ToText() + "\r\n");

        await page.OpenFileCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(page.HasPreview);
        Assert.Equal("Bob", UiHarness.Find<TextBlock>(view, "PreviewName").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-03")]
    public async Task AddContact_FileTooLarge_IsRefused()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        harness.Platform.OpenResult = new CodeFileResult(CodeFileStatus.TooLarge, null);

        await page.OpenFileCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactFileTooLarge, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(page.HasPreview);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact(Skip = "Debug builds only: Release builds have no address field.", SkipUnless = nameof(BuildInfo.IsDebug), SkipType = typeof(BuildInfo))]
    public async Task AddContact_InvalidDebugAddress_IsRefusedBeforeAnythingIsSaved()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        using var bob = KeySet.Create();
        page.CodeText = Code(bob, "Bob").ToText();
        page.CheckCommand.Execute(null);

        page.AddressText = "not an address";
        await page.AddCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactAddressInvalid, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Empty(harness.Messenger.Contacts.All);

        page.AddressText = "127.0.0.1:45999";
        await page.AddCommand.ExecuteAsync(null);

        Assert.True(harness.Messenger.Connections.TryGetAddress(bob.IdentityKey, out var address));
        Assert.Equal(45999, address.Port);
        harness.Messenger.Identity.Dispose();
    }

    private static (AddContactViewModel Page, AddContactView View) Open(UiHarness harness)
    {
        var page = harness.Shell.Pages.AddContact();
        harness.Shell.Push(page);
        var view = new AddContactView { DataContext = page };
        harness.Show(view);
        return (page, view);
    }

    private static ContactCode Code(KeySet keys, string name)
    {
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        return ContactCode.Create(keys, displayName);
    }
}
