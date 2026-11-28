using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class DeletionViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task Conversation_DeleteChat_AsksFirstThenRemovesTheMessagesOnlyOnThisDevice()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var contact = harness.ContactOf(bob);
        var aliceKey = harness.Messenger.Identity.Keys.IdentityKey;
        var page = harness.Shell.Pages.Conversation(contact);
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view);
        page.Draft = "Hi Bob";
        await page.SendCommand.ExecuteAsync(null);
        await bob.SendAsync(aliceKey, "Hi Alice", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 2 && page.Messages[0].StateText == UiText.DeliveryReceived);
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);

        page.AskDeleteChatCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "DeleteChatButton").IsVisible);
        Assert.Equal("Delete the chat with Bob? It is removed from this device only.", UiHarness.Find<TextBlock>(view, "DeleteChatQuestionText").Text);
        page.CancelDeleteChatCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.Equal(2, page.Messages.Count);

        page.AskDeleteChatCommand.Execute(null);
        await page.DeleteChatCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Empty(page.Messages);
        Assert.Empty(harness.Messenger.Chats.MessagesWith(contact.IdentityKey));
        Assert.True(UiHarness.Find<TextBlock>(view, "EmptyText").IsVisible);
        Assert.False(UiHarness.Find<Border>(view, "DeleteChatPanel").IsVisible);
        Assert.Contains("The chat with Bob was deleted on this device only.", harness.ToastTexts);
        Assert.Equal(["Hi Alice", "Hi Bob"], bob.Chats.MessagesWith(aliceKey).Select(static message => message.Text).Order(StringComparer.Ordinal));
        Assert.True(harness.Messenger.Contacts.TryGet(contact.IdentityKey, out _));
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task Conversation_WaitingMessage_IsDeletedAfterConfirmationAndNeverSent()
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
        page.Draft = "Never mind";
        await page.SendCommand.ExecuteAsync(null);
        UiHarness.Flush();
        var item = Assert.Single(page.Messages);
        Assert.Equal(UiText.DeliveryWaiting, item.StateText);
        Assert.True(item.CanDelete);
        Assert.True(item.IsDeleteVisible);
        Assert.True(UiHarness.Descendant<Button>(view, "DeleteMessageButton").IsEffectivelyVisible);

        item.AskDeleteCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(item.IsConfirmingDelete);
        Assert.False(item.IsDeleteVisible);
        Assert.Contains(UiText.MessageDeleteQuestion, UiHarness.Texts(view));

        await item.ConfirmDeleteCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Empty(page.Messages);
        Assert.Empty(harness.Messenger.Chats.WaitingFor(contact.IdentityKey));
        Assert.Contains("The waiting message to Bob was deleted.", harness.ToastTexts);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-07")]
    public async Task Conversation_ReceivedOrIncomingMessage_OffersNoDelete()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var contact = harness.ContactOf(bob);
        var page = harness.Shell.Pages.Conversation(contact);
        harness.Shell.Push(page);
        page.Draft = "Hi Bob";
        await page.SendCommand.ExecuteAsync(null);
        await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "Hi Alice", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 2 && page.Messages[0].StateText == UiText.DeliveryReceived);

        foreach (var item in page.Messages)
        {
            item.AskDeleteCommand.Execute(null);
            Assert.False(item.CanDelete);
            Assert.False(item.IsDeleteVisible);
            Assert.False(item.IsConfirmingDelete);
        }

        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task ContactList_DeleteAllData_ConfirmsThenStartsOverWithIdentityCreation()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var oldKey = harness.Messenger.Identity.Keys.IdentityKey;
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        list.OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(UiHarness.Descendant<Button>(view, "DeleteAllDataButton").IsVisible);

        list.DeleteAllDataCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(list.IsMenuOpen);
        Assert.Same(list, harness.Shell.CurrentPage);
        Assert.True(list.DeleteAll.IsConfirming);
        Assert.Equal(UiText.ContactListTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        var dialog = UiHarness.Descendant<DeleteAllDataDialog>(view, "DeleteAllDialog");
        Assert.True(UiHarness.IsShown(UiHarness.Find<Border>(dialog, "DeleteAllConfirmPanel")));
        Assert.Equal(UiText.DeleteAllDataWarning, UiHarness.Find<TextBlock>(dialog, "DeleteAllWarningText").Text);
        list.DeleteAll.CancelCommand.Execute(null);
        UiHarness.Flush();
        Assert.Same(list, harness.Shell.CurrentPage);
        Assert.False(UiHarness.IsShown(UiHarness.Find<Border>(dialog, "DeleteAllConfirmPanel")));
        Assert.NotNull(harness.Services.Messenger);

        list.DeleteAllDataCommand.Execute(null);
        await list.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        var create = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.Null(harness.Services.Messenger);
        Assert.False(harness.Services.HasStores);
        Assert.False(harness.Shell.CanGoBack);
        Assert.Contains(UiText.DeleteAllDataDone, harness.ToastTexts);
        Assert.Contains(UiText.DeleteAllDataDone, UiHarness.Texts(view));

        create.Name = "Alice again";
        await create.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.NotEqual(oldKey, harness.Messenger.Identity.Keys.IdentityKey);
        Assert.Empty(harness.Messenger.Contacts.All);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task StorageProblem_DeleteAllData_StartsOverWithIdentityCreation()
    {
        var localData = new FakeLocalData(LocalDataProblem.Damaged);
        await using var harness = UiHarness.Create(localData);
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();
        var problem = Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);
        Assert.NotNull(problem.DeleteAll);
        Assert.True(UiHarness.Descendant<Button>(view, "DeleteAllOpenButton").IsVisible);
        var dialog = UiHarness.Descendant<DeleteAllDataDialog>(view, "DeleteAllDialog");
        var confirmPanel = UiHarness.Find<Border>(dialog, "DeleteAllConfirmPanel");
        Assert.False(confirmPanel.IsVisible);

        problem.DeleteAll.OpenCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(confirmPanel.IsVisible);
        Assert.True(UiHarness.IsShown(confirmPanel));
        problem.DeleteAll.CancelCommand.Execute(null);
        Assert.False(problem.DeleteAll.IsConfirming);
        Assert.Equal(0, localData.Deleted);

        problem.DeleteAll.OpenCommand.Execute(null);
        await problem.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(1, localData.Deleted);
        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.Contains(UiText.DeleteAllDataDone, harness.ToastTexts);
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteAllData_NotEveryFileDeleted_SaysSoAndOpensTheDataAgain()
    {
        var localData = new FakeLocalData(LocalDataProblem.Damaged) { DeleteSucceeds = false };
        await using var harness = UiHarness.Create(localData);
        await harness.Shell.StartAsync(CancellationToken.None);
        var problem = Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);

        problem.DeleteAll!.OpenCommand.Execute(null);
        await problem.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Contains(UiText.DeleteAllDataIncomplete, harness.ToastTexts);
        Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(2, localData.Opened);
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task Unlock_ForgottenPassphrase_DeleteAllDataRemovesTheFilesAndStartsOver()
    {
        using var data = new SavedData();
        await CreateAsync(data, SavedData.Passphrase);
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();
        var unlock = Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
        Assert.NotNull(unlock.DeleteAll);
        Assert.True(File.Exists(data.DatabasePath));

        unlock.DeleteAll.OpenCommand.Execute(null);
        await unlock.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        var create = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.Empty(Directory.EnumerateFiles(data.Folder));
        Assert.Null(harness.Services.Messenger);
        Assert.Equal(LocalDataProtection.None, harness.Services.Protection);
        Assert.True(create.OffersPassphrase);
        create.Name = "Alice";
        await create.CreateCommand.ExecuteAsync(null);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.True(File.Exists(data.DatabasePath));
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task Locked_DeleteAllDataFromTheLockPage_RemovesTheLockAndStartsOver()
    {
        using var data = new SavedData();
        await CreateAsync(data, SavedData.Passphrase);
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var unlock = Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
        unlock.Passphrase = SavedData.Passphrase;
        await unlock.UnlockCommand.ExecuteAsync(null);
        harness.Shell.LockCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(harness.Shell.IsLocked);
        var lockPage = harness.Shell.LockPage!;

        lockPage.DeleteAll!.OpenCommand.Execute(null);
        await lockPage.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.False(harness.Shell.IsLocked);
        Assert.False(harness.Services.Lock.IsLocked);
        Assert.False(harness.Shell.CanLock);
        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.True(UiHarness.Find<ContentControl>(view, "PageHost").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "LockButton").IsVisible);
        Assert.Empty(Directory.EnumerateFiles(data.Folder));
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteAllDialog_FailingDeletion_SaysSoAndStaysOpen()
    {
        var panel = new DeleteAllDataViewModel(_ => throw new IOException("locked"));
        await using var harness = UiHarness.Create();
        var view = new DeleteAllDataDialog { DataContext = panel };
        harness.Show(view);
        Assert.False(UiHarness.IsShown(UiHarness.Find<Border>(view, "DeleteAllConfirmPanel")));

        panel.OpenCommand.Execute(null);
        await panel.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(panel.IsConfirming);
        Assert.False(panel.IsBusy);
        Assert.True(UiHarness.IsShown(UiHarness.Find<Border>(view, "DeleteAllConfirmPanel")));
        Assert.Equal(UiText.DeleteAllDataFailed, UiHarness.Find<TextBlock>(view, "DeleteAllErrorText").Text);
        Assert.Equal(UiText.DeleteAllDataConfirm, UiHarness.Find<Button>(view, "DeleteAllConfirmButton").Content);
        Assert.Equal(UiText.CommonCancel, UiHarness.Find<Button>(view, "DeleteAllCancelButton").Content);
    }

    private static async Task CreateAsync(SavedData data, string passphrase)
    {
        await using var localData = data.Open();
        var created = await localData.CreateAsync(passphrase, CancellationToken.None);
        Assert.True(created.IsOpen);
        using var identity = (await new Application.Identity.IdentityService(created.Stores.Identity).CreateAsync("Alice", CancellationToken.None)).Identity;
        Assert.NotNull(identity);
    }
}
