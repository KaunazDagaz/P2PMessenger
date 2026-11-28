using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P2PMessenger.UI.Controls;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class DialogTests
{
    [AvaloniaFact]
    public async Task Dialog_ClosedAndOpened_ShowsItsContentOnlyWhileOpen()
    {
        await using var harness = UiHarness.Create();
        var content = new Border { Height = 20 };
        var dialog = new Dialog { Content = content };
        harness.Show(dialog);
        var scrim = dialog.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_Scrim");
        Assert.False(scrim.IsEffectivelyVisible);
        Assert.False(UiHarness.IsShown(content));

        dialog.IsOpen = true;
        UiHarness.Flush();
        Assert.True(scrim.IsEffectivelyVisible);
        Assert.True(UiHarness.IsShown(content));

        dialog.IsOpen = false;
        UiHarness.Flush();
        Assert.False(scrim.IsEffectivelyVisible);
        Assert.False(UiHarness.IsShown(content));
    }

    [AvaloniaFact]
    public async Task Dialog_EscapeKey_RunsTheCancelCommandOnlyWhileOpenAndWhenItCanRun()
    {
        await using var harness = UiHarness.Create();
        var cancel = new CountingCommand();
        var dialog = new Dialog { CancelCommand = cancel, Content = new Border { Height = 20 } };
        harness.Show(dialog);
        dialog.RaiseEvent(KeyDown(dialog, Key.Escape));
        Assert.Equal(0, cancel.Runs);

        dialog.IsOpen = true;
        UiHarness.Flush();
        dialog.RaiseEvent(KeyDown(dialog, Key.Escape));
        dialog.RaiseEvent(KeyDown(dialog, Key.Enter));
        Assert.Equal(1, cancel.Runs);

        cancel.Enabled = false;
        dialog.RaiseEvent(KeyDown(dialog, Key.Escape));
        Assert.Equal(1, cancel.Runs);
    }

    [AvaloniaFact]
    public async Task Dialog_ClickOutsideTheCard_RunsTheCancelCommandAndClickInsideDoesNot()
    {
        await using var harness = UiHarness.Create();
        var cancel = new CountingCommand();
        var inner = new Border { Height = 40, Background = Brushes.Transparent };
        var dialog = new Dialog { IsOpen = true, CancelCommand = cancel, CardMaxWidth = 200, Content = inner };
        var window = harness.Show(dialog, 600, 400);

        window.MouseDown(new Point(300, 200), MouseButton.Left);
        window.MouseUp(new Point(300, 200), MouseButton.Left);
        Assert.Equal(0, cancel.Runs);

        window.MouseDown(new Point(20, 20), MouseButton.Left);
        window.MouseUp(new Point(20, 20), MouseButton.Left);
        Assert.Equal(1, cancel.Runs);
    }

    [AvaloniaFact]
    public async Task MainView_WhileADialogIsOpen_DimsTheHeaderAndStopsWhenItCloses()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var scrim = UiHarness.Find<Border>(view, "HeaderScrim");
        Assert.False(scrim.IsVisible);

        list.DeleteAll.OpenCommand.Execute(null);
        UiHarness.Flush();
        Assert.Contains("dialogOpen", view.Classes);
        Assert.True(scrim.IsVisible);

        list.DeleteAll.CancelCommand.Execute(null);
        UiHarness.Flush();
        Assert.DoesNotContain("dialogOpen", view.Classes);
        Assert.False(scrim.IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task MainView_PageWithAnOpenDialogIsLeft_StopsDimmingTheHeader()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        list.DeleteAll.OpenCommand.Execute(null);
        UiHarness.Flush();
        Assert.Contains("dialogOpen", view.Classes);

        await list.DeleteAll.ConfirmCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.DoesNotContain("dialogOpen", view.Classes);
    }

    [AvaloniaFact]
    public async Task SettingsMenu_OpenedWithItsButton_ShowsTheItemsUnderTheButtonWithoutDimmingTheHeader()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var main = new MainView { DataContext = harness.Shell };
        var window = harness.Show(main, 900, 760);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var button = UiHarness.Descendant<Button>(main, "MenuButton");
        var menu = UiHarness.Descendant<Dialog>(main, "SettingsMenu");
        var card = menu.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_Card");
        Assert.False(UiHarness.IsShown(card));

        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        UiHarness.Flush();

        Assert.True(list.IsMenuOpen);
        Assert.True(UiHarness.IsShown(card));
        Assert.True(UiHarness.IsShown(UiHarness.Descendant<Button>(main, "DeleteAllDataButton")));
        Assert.DoesNotContain("dialogOpen", main.Classes);
        var cardAt = card.TranslatePoint(default, window)!.Value;
        var buttonAt = button.TranslatePoint(default, window)!.Value;
        Assert.InRange(Math.Abs((cardAt.X + card.Bounds.Width) - (buttonAt.X + button.Bounds.Width)), 0, 1);
        Assert.True(cardAt.Y >= buttonAt.Y + button.Bounds.Height);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsMenu_EscapeOrAClickOutsideTheCard_ClosesIt()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var main = new MainView { DataContext = harness.Shell };
        var window = harness.Show(main, 900, 760);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var menu = UiHarness.Descendant<Dialog>(main, "SettingsMenu");

        list.OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        menu.RaiseEvent(KeyDown(menu, Key.Escape));
        Assert.False(list.IsMenuOpen);

        list.OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        window.MouseDown(new Point(20, 400), MouseButton.Left);
        window.MouseUp(new Point(20, 400), MouseButton.Left);
        Assert.False(list.IsMenuOpen);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsMenu_ChoosingAnItemOrLeavingThePage_ClosesIt()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);

        list.OpenMenuCommand.Execute(null);
        list.PassphraseCommand.Execute(null);
        Assert.False(list.IsMenuOpen);
        Assert.IsType<PassphraseViewModel>(harness.Shell.CurrentPage);

        harness.Shell.GoBack();
        list.OpenMenuCommand.Execute(null);
        list.AddContactCommand.Execute(null);
        Assert.False(list.IsMenuOpen);
        Assert.IsType<AddContactViewModel>(harness.Shell.CurrentPage);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Conversation_WaitingMessage_DeleteIsOfferedInTheBubbleAndConfirmedInAPopup()
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
        Assert.Null(page.DeletingMessage);
        Assert.False(page.HasDeletingMessage);
        Assert.False(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeleteMessagePanel")));

        item.AskDeleteCommand.Execute(null);
        UiHarness.Flush();
        Assert.Same(item, page.DeletingMessage);
        Assert.True(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeleteMessagePanel")));
        Assert.Same(item, UiHarness.Find<StackPanel>(view, "DeleteMessagePanel").DataContext);

        item.CancelDeleteCommand.Execute(null);
        UiHarness.Flush();
        Assert.Null(page.DeletingMessage);
        Assert.False(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeleteMessagePanel")));

        item.AskDeleteCommand.Execute(null);
        await item.ConfirmDeleteCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Empty(page.Messages);
        Assert.Null(page.DeletingMessage);
        Assert.False(UiHarness.IsShown(UiHarness.Find<StackPanel>(view, "DeleteMessagePanel")));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Dialog_TheTopOneCloses_GivesTheFocusBackToTheOneBelow()
    {
        await using var harness = UiHarness.Create();
        var below = new Dialog { IsOpen = true, Content = new Border { Height = 20 } };
        var above = new Dialog { IsOpen = true, Content = new Border { Height = 20 } };
        harness.Show(new Grid { Children = { below, above } });
        Dispatcher.UIThread.RunJobs();
        var belowCard = below.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_Card");
        var aboveCard = above.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_Card");
        Assert.True(aboveCard.IsFocused);

        above.IsOpen = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(belowCard.IsFocused);
    }

    private static KeyEventArgs KeyDown(Dialog dialog, Key key) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Source = dialog, Key = key };

    private sealed class CountingCommand : System.Windows.Input.ICommand
    {
        private bool enabled = true;

        public event EventHandler? CanExecuteChanged;

        public int Runs { get; private set; }

        public bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool CanExecute(object? parameter) => enabled;

        public void Execute(object? parameter) => Runs++;
    }
}
