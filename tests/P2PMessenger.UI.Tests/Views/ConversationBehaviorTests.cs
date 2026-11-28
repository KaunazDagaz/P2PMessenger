using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class ConversationBehaviorTests
{
    [AvaloniaFact]
    public async Task DraftBox_EnterSendsTheMessageAndShiftEnterStartsANewLine()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var page = harness.Shell.Pages.Conversation(harness.ContactOf(bob));
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        var window = harness.Show(view);
        var box = UiHarness.Find<TextBox>(view, "DraftBox");
        box.Focus();

        window.KeyTextInput("first");
        window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
        UiHarness.Flush();
        Assert.Empty(page.Messages);
        window.KeyTextInput("second");
        Assert.Equal("first" + Environment.NewLine + "second", page.Draft);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await UiHarness.WaitUntilAsync(() => page.Messages.Count == 1);

        Assert.Equal("first" + Environment.NewLine + "second", page.Messages[0].Text);
        Assert.Equal(string.Empty, page.Draft);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task DraftBox_EnterOnAnEmptyMessage_SendsNothingAndAddsNoLine()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var page = harness.Shell.Pages.Conversation(harness.ContactOf(bob));
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        var window = harness.Show(view);
        UiHarness.Find<TextBox>(view, "DraftBox").Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        UiHarness.Flush();

        Assert.Empty(page.Messages);
        Assert.Equal(string.Empty, page.Draft);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Messages_WhileTheReaderIsAtTheEnd_StayInViewAndWhileScrolledUpDoNotJump()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var page = harness.Shell.Pages.Conversation(harness.ContactOf(bob));
        var view = new ConversationView { DataContext = page };
        harness.Shell.Push(page);
        harness.Show(view, 600, 500);
        var viewer = view.GetVisualDescendants().OfType<ScrollViewer>().First(static scroll => scroll.Content is ItemsControl);
        await SendAsync(page, 25);
        UiHarness.Flush();
        Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
        Assert.True(IsAtEnd(viewer));

        await SendAsync(page, 3);
        UiHarness.Flush();
        Assert.True(IsAtEnd(viewer));

        viewer.ScrollToHome();
        UiHarness.Flush();
        await SendAsync(page, 2);
        UiHarness.Flush();
        Assert.Equal(0, viewer.Offset.Y);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task MainView_KeyboardOpened_RaisesTheMessageBoxAboveIt()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main, 500, 800);
        harness.Shell.Push(harness.Shell.Pages.Conversation(harness.ContactOf(bob)));
        UiHarness.Flush();
        var box = UiHarness.Descendant<TextBox>(main, "DraftBox");
        double before = BottomOf(box, main);

        harness.Shell.KeyboardInset = 300;
        UiHarness.Flush();

        Assert.Equal(before - 300, BottomOf(box, main), 1);

        harness.Shell.KeyboardInset = 0;
        UiHarness.Flush();
        Assert.Equal(before, BottomOf(box, main), 1);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task MainView_BackRequested_ClosesAnOpenPopupFirstThenGoesBackAPageAndLeavesTheFirstPageToTheSystem()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var main = new MainView { DataContext = harness.Shell };
        var window = harness.Show(main, 500, 800);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.False(Back(window));

        list.MyCodeCommand.Execute(null);
        UiHarness.Flush();
        Assert.IsType<ProfileViewModel>(harness.Shell.CurrentPage);
        Assert.True(Back(window));
        Assert.Same(list, harness.Shell.CurrentPage);

        list.OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(Back(window));
        Assert.False(list.IsMenuOpen);
        Assert.Same(list, harness.Shell.CurrentPage);

        list.DeleteAllDataCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(list.DeleteAll.IsConfirming);
        Assert.True(Back(window));
        Assert.False(list.DeleteAll.IsConfirming);
        Assert.False(Back(window));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task MainView_BackRequestedWhileLocked_IsLeftToTheSystem()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var main = new MainView { DataContext = harness.Shell };
        var window = harness.Show(main, 500, 800);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).MyCodeCommand.Execute(null);
        harness.Shell.LockPage = harness.Shell.Pages.Unlock(false, (_, _) => Task.FromResult(true));
        UiHarness.Flush();

        Assert.False(Back(window));
        Assert.IsType<ProfileViewModel>(harness.Shell.CurrentPage);
        harness.Messenger.Identity.Dispose();
    }

    private static bool Back(Window window)
    {
        var args = new RoutedEventArgs(TopLevel.BackRequestedEvent);
        window.RaiseEvent(args);
        UiHarness.Flush();
        return args.Handled;
    }

    private static bool IsAtEnd(ScrollViewer viewer) =>
        viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - 2;

    private static double BottomOf(Control control, Visual root) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height), root)!.Value.Y;

    private static async Task SendAsync(ConversationViewModel page, int count)
    {
        for (int index = 0; index < count; index++)
        {
            page.Draft = $"Message number {index}, written to make the list long enough to scroll.";
            await page.SendCommand.ExecuteAsync(null);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
