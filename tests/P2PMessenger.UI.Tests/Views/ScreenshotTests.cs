using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using P2PMessenger.Application;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("P2PM_SCREENSHOTS");

    private static readonly Variant[] Variants =
    [
        new("desktop-light", ThemeVariant.Light, 900, 760),
        new("desktop-dark", ThemeVariant.Dark, 900, 760),
        new("phone-light", ThemeVariant.Light, 360, 720),
        new("phone-dark", ThemeVariant.Dark, 360, 720),
    ];

    [AvaloniaFact]
    public async Task Capture_EveryMainScreen_WritesPicturesWhenAFolderIsGiven()
    {
        Assert.SkipUnless(Folder is not null, "Set P2PM_SCREENSHOTS to a folder to write the pictures.");
        Directory.CreateDirectory(Folder!);
        string? only = Environment.GetEnvironmentVariable("P2PM_SCREENSHOT_VARIANT");
        string? screens = Environment.GetEnvironmentVariable("P2PM_SCREENSHOT_SCREENS");
        foreach (Variant variant in Variants.Where(variant => only is null || variant.Name == only))
        {
            Avalonia.Application.Current!.RequestedThemeVariant = variant.Theme;
            foreach ((string name, Func<Variant, Task> capture) in Scenarios.Where(scenario => screens is null || screens.Split(',').Contains(scenario.Name)))
            {
                await capture(variant);
            }
        }

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static (string Name, Func<Variant, Task> Capture)[] Scenarios =>
    [
        ("first-run", FirstRunAsync),
        ("unlock", UnlockAsync),
        ("storage-problem", StorageProblemAsync),
        ("contacts-empty", ContactsEmptyAsync),
        ("contacts", ContactsAsync),
        ("menu", MenuAsync),
        ("conversation", ConversationAsync),
        ("group", GroupAsync),
        ("profile", ProfileAsync),
        ("add-in-person", AddInPersonAsync),
        ("add-contact", AddContactAsync),
        ("passphrase", PassphraseAsync),
        ("verify", VerifyAsync),
        ("background", BackgroundAsync),
        ("new-group", NewGroupAsync),
        ("delete-all", DeleteAllAsync),
        ("delete-chat", DeleteChatAsync),
        ("message-details", MessageDetailsAsync),
        ("members", MembersAsync),
        ("remove-member", RemoveMemberAsync),
    ];

    private static void Save(Window window, string screen, Variant variant)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(Folder!, $"{screen}-{variant.Name}.png"), PngBitmapEncoderOptions.Default);
    }

    private static Window Open(UiHarness harness, Variant variant)
    {
        harness.Shell.IsStarting = false;
        var view = new MainView { DataContext = harness.Shell };
        return harness.Show(view, variant.Width, variant.Height);
    }

    private static async Task FirstRunAsync(Variant variant)
    {
        await using var harness = UiHarness.Create();
        var window = Open(harness, variant);
        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();
        Save(window, "first-run", variant);
    }

    private static async Task UnlockAsync(Variant variant)
    {
        await using var harness = UiHarness.Create();
        var window = Open(harness, variant);
        harness.Shell.ResetTo(harness.Shell.Pages.Unlock(true, (_, _) => Task.FromResult(false)));
        await Task.CompletedTask;
        Save(window, "unlock", variant);
    }

    private static async Task StorageProblemAsync(Variant variant)
    {
        await using var harness = UiHarness.Create();
        var window = Open(harness, variant);
        harness.Shell.ResetTo(harness.Shell.Pages.StorageProblem(LocalDataProblem.KeyStoreUnavailable));
        await Task.CompletedTask;
        Save(window, "storage-problem", variant);
    }

    private static async Task ContactsEmptyAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var window = Open(harness, variant);
        UiHarness.Flush();
        Save(window, "contacts-empty", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task ContactsAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var carol = await harness.ConnectPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        using var dmitri = KeySet.Create();
        await harness.AddContactAsync(dmitri, "Dmitri Ivanov");
        await harness.Messenger.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "See you at six", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Chats.UnreadCount(bob.Identity.Keys.IdentityKey) > 0);
        var window = Open(harness, variant);
        harness.Shell.ResetTo(harness.Shell.Pages.ContactList());
        UiHarness.Flush();
        Save(window, "contacts", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task MenuAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var dmitri = KeySet.Create();
        await harness.AddContactAsync(dmitri, "Dmitri Ivanov");
        var window = Open(harness, variant);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        list.OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "menu", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task ConversationAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var contact = harness.ContactOf(bob);
        await harness.Messenger.SendAsync(contact.IdentityKey, "Hi Bob, are you free tonight?", CancellationToken.None);
        await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "Yes, around six works for me.", CancellationToken.None);
        await harness.Messenger.SendAsync(contact.IdentityKey, "Great, see you at the station then.", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Chats.MessagesWith(contact.IdentityKey).Count == 3);
        var window = Open(harness, variant);
        harness.Shell.Push(harness.Shell.Pages.Conversation(contact));
        UiHarness.Flush();
        Save(window, "conversation", variant);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task GroupAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var carol = await harness.ConnectPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        var created = await harness.Messenger.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Groups.TryGet(groupId, out var group) && group.State == P2PMessenger.Application.Groups.GroupState.Created);
        await harness.Messenger.SendToGroupAsync(groupId, "Meeting point is the north gate.", CancellationToken.None);
        await bob.SendToGroupAsync(groupId, "On my way.", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.GroupChats.MessagesIn(groupId).Count == 2);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.GroupConversation(groupId);
        harness.Shell.Push(page);
        UiHarness.Flush();
        Save(window, "group", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task ProfileAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var window = Open(harness, variant);
        harness.Shell.Push(harness.Shell.Pages.Profile());
        await Task.CompletedTask;
        Save(window, "profile", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task AddInPersonAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var window = Open(harness, variant);
        harness.Shell.Push(harness.Shell.Pages.AddInPerson());
        await Task.CompletedTask;
        Save(window, "add-in-person", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task AddContactAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.StartPeerAsync("Bob Miller");
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.AddContact();
        harness.Shell.Push(page);
        page.CodeText = bob.Identity.CodeText;
        page.CheckCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "add-contact", variant);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task PassphraseAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var window = Open(harness, variant);
        harness.Shell.Push(harness.Shell.Pages.Passphrase());
        await Task.CompletedTask;
        Save(window, "passphrase", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task VerifyAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob Miller");
        var window = Open(harness, variant);
        harness.Shell.Push(harness.Shell.Pages.VerifyContact(contact));
        UiHarness.Flush();
        Save(window, "verify", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task BackgroundAsync(Variant variant)
    {
        var fake = new FakeBackground();
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        var window = Open(harness, variant);
        harness.Shell.ResetTo(harness.Shell.Pages.Background(true));
        await Task.CompletedTask;
        Save(window, "background", variant);
    }

    private static async Task NewGroupAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var carol = await harness.ConnectPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.NewGroup();
        harness.Shell.Push(page);
        page.Name = "Field team";
        foreach (var candidate in page.Candidates)
        {
            candidate.IsSelected = true;
        }

        UiHarness.Flush();
        Save(window, "new-group", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task DeleteAllAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var window = Open(harness, variant);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        list.DeleteAll.OpenCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "delete-all", variant);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task DeleteChatAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var contact = harness.ContactOf(bob);
        await harness.Messenger.SendAsync(contact.IdentityKey, "Hi Bob, are you free tonight?", CancellationToken.None);
        await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "Yes, around six works for me.", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Chats.MessagesWith(contact.IdentityKey).Count == 2);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.Conversation(contact);
        harness.Shell.Push(page);
        page.AskDeleteChatCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "delete-chat", variant);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task MessageDetailsAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (bob, carol, groupId) = await PrepareGroupAsync(harness);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.GroupConversation(groupId);
        harness.Shell.Push(page);
        UiHarness.Flush();
        page.Messages.First(static message => message.IsOwn).ToggleDetailsCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "message-details", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task MembersAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (bob, carol, groupId) = await PrepareGroupAsync(harness);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.GroupConversation(groupId);
        harness.Shell.Push(page);
        page.ToggleMembersCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "members", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task RemoveMemberAsync(Variant variant)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (bob, carol, groupId) = await PrepareGroupAsync(harness);
        var window = Open(harness, variant);
        var page = harness.Shell.Pages.GroupConversation(groupId);
        harness.Shell.Push(page);
        page.ToggleMembersCommand.Execute(null);
        page.Members.First(static member => member.CanRemove).AskRemoveCommand.Execute(null);
        UiHarness.Flush();
        Save(window, "remove-member", variant);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task<(Messenger Bob, Messenger Carol, GroupId GroupId)> PrepareGroupAsync(UiHarness harness)
    {
        var bob = await harness.ConnectPeerAsync("Bob Miller");
        var carol = await harness.ConnectPeerAsync("Carol");
        await KnowEachOtherAsync(bob, carol);
        var created = await harness.Messenger.CreateGroupAsync("Field team", [bob.Identity.Keys.IdentityKey, carol.Identity.Keys.IdentityKey], CancellationToken.None);
        var groupId = created.Group!.Id;
        await UiHarness.WaitUntilAsync(() => harness.Messenger.Groups.TryGet(groupId, out var group) && group.State == P2PMessenger.Application.Groups.GroupState.Created);
        await harness.Messenger.SendToGroupAsync(groupId, "Meeting point is the north gate.", CancellationToken.None);
        await bob.SendToGroupAsync(groupId, "On my way.", CancellationToken.None);
        await UiHarness.WaitUntilAsync(() => harness.Messenger.GroupChats.MessagesIn(groupId).Count == 2);
        return (bob, carol, groupId);
    }

    private static async Task KnowEachOtherAsync(Messenger first, Messenger second)
    {
        Assert.True((await first.Contacts.AddAsync(first.Contacts.Check(second.Identity.CodeText), CancellationToken.None)).IsSaved);
        Assert.True((await second.Contacts.AddAsync(second.Contacts.Check(first.Identity.CodeText), CancellationToken.None)).IsSaved);
        await UiHarness.WaitUntilAsync(() =>
            first.Contacts.TryGet(second.Identity.Keys.IdentityKey, out var atFirst) && atFirst.IsAdded
            && second.Contacts.TryGet(first.Identity.Keys.IdentityKey, out var atSecond) && atSecond.IsAdded);
    }

    private readonly record struct Variant(string Name, ThemeVariant Theme, double Width, double Height);
}
