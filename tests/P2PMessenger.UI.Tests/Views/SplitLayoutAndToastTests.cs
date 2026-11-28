using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class SplitLayoutAndToastTests
{
    [AvaloniaFact]
    public async Task WideWindow_ShowsTheListBesideTheOpenChatAndNeverLeavesTheList()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main, 1100, 760);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var master = UiHarness.Find<ContentControl>(main, "MasterHost");
        var detail = UiHarness.Find<ContentControl>(main, "PageHost");
        var placeholder = UiHarness.Find<Border>(main, "DetailPlaceholder");

        Assert.True(harness.Shell.IsSplit);
        Assert.Same(list, master.Content);
        Assert.Null(detail.Content);
        Assert.True(placeholder.IsVisible);
        Assert.Equal(UiText.ContactListTitle, UiHarness.Find<TextBlock>(main, "MasterTitleText").Text);
        Assert.False(UiHarness.Find<Button>(main, "BackButton").IsVisible);

        var bobItem = list.Contacts.Single();
        bobItem.OpenCommand.Execute(null);
        UiHarness.Flush();

        var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        Assert.Same(conversation, detail.Content);
        Assert.Same(list, master.Content);
        Assert.False(placeholder.IsVisible);
        Assert.Equal("Bob", UiHarness.Find<TextBlock>(main, "TitleText").Text);
        Assert.True(UiHarness.Find<Button>(main, "BackButton").IsVisible);
        Assert.True(UiHarness.IsShown(UiHarness.Find<Border>(main, "MasterPane")));
        Assert.True(list.Contacts.Single().IsSelected);

        var carol = await harness.ConnectPeerAsync("Carol");
        await UiHarness.WaitUntilAsync(() => list.Contacts.Count == 2);
        Assert.Same(conversation, harness.Shell.CurrentPage);

        list.Contacts.Single(static item => item.Name == "Carol").OpenCommand.Execute(null);
        UiHarness.Flush();
        var second = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        Assert.NotSame(conversation, second);
        Assert.Equal(["Carol"], list.Contacts.Where(static item => item.IsSelected).Select(static item => item.Name));
        harness.Shell.GoBack();
        UiHarness.Flush();
        Assert.Same(list, harness.Shell.CurrentPage);
        Assert.DoesNotContain(list.Contacts, static item => item.IsSelected);
        Assert.Null(detail.Content);
        Assert.True(placeholder.IsVisible);
        Assert.False(harness.Shell.CanGoBack);
        await UiHarness.StopPeerAsync(bob);
        await UiHarness.StopPeerAsync(carol);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task WideWindow_APopupOnOneSide_DimsTheOtherSideToo()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main, 1100, 760);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var masterShade = UiHarness.Find<Border>(main, "MasterShade");
        var detailShade = UiHarness.Find<Border>(main, "DetailShade");
        list.Contacts.Single().OpenCommand.Execute(null);
        UiHarness.Flush();
        var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        Assert.False(masterShade.IsVisible);
        Assert.False(detailShade.IsVisible);

        conversation.AskDeleteChatCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(masterShade.IsVisible);
        Assert.False(detailShade.IsVisible);

        conversation.CancelDeleteChatCommand.Execute(null);
        list.DeleteAllDataCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(masterShade.IsVisible);
        Assert.True(detailShade.IsVisible);

        list.DeleteAll.CancelCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(detailShade.IsVisible);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ResizedAcrossTheBreakpoint_KeepsTheOpenChatAndAddsOrDropsTheList()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var main = new MainView { DataContext = harness.Shell };
        Window window = harness.Show(main, 700, 760);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        list.Contacts.Single().OpenCommand.Execute(null);
        UiHarness.Flush();
        var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
        var master = UiHarness.Find<Border>(main, "MasterPane");
        var detail = UiHarness.Find<ContentControl>(main, "PageHost");
        Assert.False(harness.Shell.IsSplit);
        Assert.False(master.IsVisible);
        Assert.Same(conversation, detail.Content);

        window.Width = 1100;
        UiHarness.Flush();
        Assert.True(harness.Shell.IsSplit);
        Assert.True(UiHarness.IsShown(master));
        Assert.Same(conversation, detail.Content);
        Assert.Same(list, UiHarness.Find<ContentControl>(main, "MasterHost").Content);

        window.Width = 700;
        UiHarness.Flush();
        Assert.False(harness.Shell.IsSplit);
        Assert.False(master.IsVisible);
        Assert.Same(conversation, harness.Shell.CurrentPage);
        Assert.Same(conversation, detail.Content);
        harness.Shell.GoBack();
        Assert.Same(list, detail.Content);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task WideWindowWithoutAContactList_ShowsTheFirstPageAcrossTheWindow()
    {
        await using var harness = UiHarness.Create();
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main, 1100, 760);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.False(harness.Shell.IsSplit);
        Assert.False(UiHarness.Find<Border>(main, "MasterPane").IsVisible);
        Assert.False(UiHarness.Find<Border>(main, "DetailPlaceholder").IsVisible);
        Assert.Same(harness.Shell.CurrentPage, UiHarness.Find<ContentControl>(main, "PageHost").Content);
    }

    [AvaloniaFact]
    public async Task Toast_Shown_AppearsAboveThePageWithoutMovingIt()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main, 800, 700);
        var host = UiHarness.Find<ContentControl>(main, "PageHost");
        var title = UiHarness.Find<TextBlock>(main, "TitleText");
        Point pageBefore = host.TranslatePoint(default, main)!.Value;
        Point titleBefore = title.TranslatePoint(default, main)!.Value;

        harness.Services.Toasts.Show(ToastKind.Success, "Bob is added.");
        harness.Services.Toasts.Show(ToastKind.Error, "Something failed.");
        UiHarness.Flush();

        Assert.Contains("Bob is added.", UiHarness.Texts(main));
        Assert.Contains("Something failed.", UiHarness.Texts(main));
        Assert.Equal(pageBefore, host.TranslatePoint(default, main)!.Value);
        Assert.Equal(titleBefore, title.TranslatePoint(default, main)!.Value);
        var toast = UiHarness.Find<ItemsControl>(main, "ToastHost");
        Assert.True(toast.Bounds.Height > 0);
        Assert.True(toast.TranslatePoint(new Point(toast.Bounds.Width, 0), main)!.Value.X <= main.Bounds.Width);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Toast_Shown_GoesAwayByItselfUnlessItIsSticky()
    {
        await using var harness = UiHarness.Create();
        var toasts = harness.Services.Toasts;
        toasts.Lifetime = TimeSpan.FromMilliseconds(60);
        toasts.ErrorLifetime = TimeSpan.FromMilliseconds(60);

        toasts.Show(ToastKind.Success, "Saved");
        toasts.Show(ToastKind.Warning, "Nothing is saved", sticky: true);
        Assert.Equal(2, toasts.Items.Count);

        await UiHarness.WaitUntilAsync(() => toasts.Items.Count == 1);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        UiHarness.Flush();

        var left = Assert.Single(toasts.Items);
        Assert.Equal("Nothing is saved", left.Text);
        left.DismissCommand.Execute(null);
        Assert.Empty(toasts.Items);
    }

    [AvaloniaFact]
    public async Task Toast_ShownAgainOrInAFlood_KeepsOneOfAKindAndTheNewest()
    {
        await using var harness = UiHarness.Create();
        var toasts = harness.Services.Toasts;

        toasts.Show(ToastKind.Success, "Same");
        toasts.Show(ToastKind.Success, "Same");
        Assert.Single(toasts.Items);

        for (int index = 0; index < ToastCenter.MaxVisible + 2; index++)
        {
            toasts.Show(ToastKind.Success, $"Toast {index}");
        }

        Assert.Equal(ToastCenter.MaxVisible, toasts.Items.Count);
        Assert.Equal($"Toast {ToastCenter.MaxVisible + 1}", toasts.Items[^1].Text);
        Assert.DoesNotContain("Toast 0", toasts.Items.Select(static toast => toast.Text));
    }
}
