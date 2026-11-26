using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class GroupTests
{
    private static readonly CompositeFormat ReceivedFormat = CompositeFormat.Parse(UiText.GroupDeliveryReceivedFormat);

    [AvaloniaFact]
    [Trait("Story", "US-12")]
    [Trait("Story", "US-11")]
    [Trait("Story", "US-21")]
    public async Task ThreePeopleWhoAddedEachOther_CreateAGroupChatAndOneDeletesTheChat_TheOthersKeepTheirCopiesAndTheGroupGoesOn()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await using Device carol = new(hub, "Carol");
        foreach (Device device in new[] { alice, bob, carol })
        {
            await device.LaunchAsync();
            await Flows.CreateIdentityAsync(device);
        }

        foreach ((Device first, Device second) in new[] { (alice, bob), (alice, carol), (bob, carol) })
        {
            await Flows.AddEachOtherAsync(first, second);
            await Flows.WaitUntilConnectedAsync(first, second);
            await Flows.GoBackToContactsAsync(first);
            await Flows.GoBackToContactsAsync(second);
        }

        alice.ClickButton("NewGroupButton");
        await alice.WaitForTitleAsync(UiText.NewGroupTitle);
        alice.Type("NameBox", "Field team");
        alice.Click(Candidate(alice, "Bob"));
        alice.Click(Candidate(alice, "Carol"));
        alice.ClickButton("CreateButton");
        await alice.WaitForTitleAsync("Field team");
        await alice.WaitUntilAsync(() => alice.TryNamed<TextBox>("DraftBox") is not null, "Alice can write once Bob and Carol have joined");

        await Flows.SendAsync(alice, "Meet at the bridge at six.");
        await alice.WaitForTextAsync(string.Format(CultureInfo.InvariantCulture, ReceivedFormat, 2, 2));

        foreach (Device member in new[] { bob, carol })
        {
            await OpenGroupAsync(member, "Field team");
            await member.WaitForTextAsync("Meet at the bridge at six.");
            Assert.Contains(UiText.GroupCreatedByFormat.Replace("{0}", "Alice", StringComparison.Ordinal), member.Texts());
        }

        await bob.WaitUntilAsync(() => bob.TryNamed<TextBox>("DraftBox") is not null, "Bob can write once he knows that everyone has joined");
        await Flows.SendAsync(bob, "On my way.");
        await carol.WaitForTextAsync("On my way.");
        await alice.WaitForTextAsync("On my way.");
        await bob.WaitForTextAsync(string.Format(CultureInfo.InvariantCulture, ReceivedFormat, 2, 2));

        alice.ClickButton("DeleteChatButton");
        await alice.WaitUntilAsync(() => alice.TryNamed<Button>("DeleteChatConfirmButton") is { IsEffectivelyVisible: true }, "Alice is asked before the chat is deleted");
        alice.ClickButton("DeleteChatConfirmButton");
        await alice.WaitForTextAsync(UiText.GroupChatDeleted);
        Assert.False(alice.Shows("Meet at the bridge at six."));
        Assert.False(alice.Shows("On my way."));
        Assert.True(bob.Shows("Meet at the bridge at six."));
        Assert.True(carol.Shows("On my way."));

        await carol.WaitUntilAsync(() => carol.TryNamed<TextBox>("DraftBox") is not null, "Carol can write once she knows that everyone has joined");
        await Flows.SendAsync(carol, "Still there?");
        await alice.WaitForTextAsync("Still there?");
    }

    [AvaloniaFact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-04")]
    public async Task AliceWritesWhileCarolIsClosed_AliceClosesAndCarolMeetsBob_CarolSeesAlicesMessage()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await using Device carol = new(hub, "Carol");
        foreach (Device device in new[] { alice, bob, carol })
        {
            await device.LaunchAsync();
            await Flows.CreateIdentityAsync(device);
        }

        foreach ((Device first, Device second) in new[] { (alice, bob), (alice, carol), (bob, carol) })
        {
            await Flows.AddEachOtherAsync(first, second);
            await Flows.WaitUntilConnectedAsync(first, second);
            await Flows.GoBackToContactsAsync(first);
            await Flows.GoBackToContactsAsync(second);
        }

        alice.ClickButton("NewGroupButton");
        await alice.WaitForTitleAsync(UiText.NewGroupTitle);
        alice.Type("NameBox", "Field team");
        alice.Click(Candidate(alice, "Bob"));
        alice.Click(Candidate(alice, "Carol"));
        alice.ClickButton("CreateButton");
        await alice.WaitForTitleAsync("Field team");
        await alice.WaitUntilAsync(() => alice.TryNamed<TextBox>("DraftBox") is not null, "Alice can write once Bob and Carol have joined");
        foreach (Device member in new[] { bob, carol })
        {
            await OpenGroupAsync(member, "Field team");
            await member.WaitUntilAsync(() => member.TryNamed<TextBox>("DraftBox") is not null, $"{member.Name} can write once everyone has joined");
        }

        await carol.QuitAsync();
        await Flows.SendAsync(alice, "Meet at the north gate.");
        await alice.WaitForTextAsync(string.Format(CultureInfo.InvariantCulture, ReceivedFormat, 1, 2));
        await bob.WaitForTextAsync("Meet at the north gate.");

        await alice.QuitAsync();
        await carol.LaunchAsync();
        await OpenGroupAsync(carol, "Field team");
        await carol.WaitForTextAsync("Meet at the north gate.");
        var senders = carol.Named<ItemsControl>("MessageItems").GetVisualDescendants().OfType<TextBlock>()
            .SelectMany(static block => block.Inlines?.OfType<Run>() ?? [])
            .Select(static run => run.Text)
            .ToArray();
        Assert.Contains("Alice", senders);
        Assert.DoesNotContain("Bob", senders);
    }

    [AvaloniaFact]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-07")]
    public async Task AliceWritesWhileCarolIsClosed_CarolGetsMessageFromBobAndAliceRejoinsBob_AlicesMessageDetailsShowCarolReceived()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await using Device carol = new(hub, "Carol");
        foreach (Device device in new[] { alice, bob, carol })
        {
            await device.LaunchAsync();
            await Flows.CreateIdentityAsync(device);
        }

        foreach ((Device first, Device second) in new[] { (alice, bob), (alice, carol), (bob, carol) })
        {
            await Flows.AddEachOtherAsync(first, second);
            await Flows.WaitUntilConnectedAsync(first, second);
            await Flows.GoBackToContactsAsync(first);
            await Flows.GoBackToContactsAsync(second);
        }

        alice.ClickButton("NewGroupButton");
        await alice.WaitForTitleAsync(UiText.NewGroupTitle);
        alice.Type("NameBox", "Field team");
        alice.Click(Candidate(alice, "Bob"));
        alice.Click(Candidate(alice, "Carol"));
        alice.ClickButton("CreateButton");
        await alice.WaitForTitleAsync("Field team");
        await alice.WaitUntilAsync(() => alice.TryNamed<TextBox>("DraftBox") is not null, "Alice can write once Bob and Carol have joined");
        foreach (Device member in new[] { bob, carol })
        {
            await OpenGroupAsync(member, "Field team");
            await member.WaitUntilAsync(() => member.TryNamed<TextBox>("DraftBox") is not null, $"{member.Name} can write once everyone has joined");
        }

        await carol.QuitAsync();
        await Flows.SendAsync(alice, "North gate is open.");
        await alice.WaitForTextAsync(string.Format(CultureInfo.InvariantCulture, ReceivedFormat, 1, 2));
        await bob.WaitForTextAsync("North gate is open.");

        alice.Click(DeliveryButton(alice));
        await alice.WaitForTextAsync("Bob: Received by their app");
        Assert.True(alice.Shows("Carol: Waiting"));

        await alice.QuitAsync();
        await carol.LaunchAsync();
        await OpenGroupAsync(carol, "Field team");
        await carol.WaitForTextAsync("North gate is open.");
        await Task.Delay(300);
        await carol.QuitAsync();

        await alice.LaunchAsync();
        await OpenGroupAsync(alice, "Field team");
        await alice.WaitForTextAsync(string.Format(CultureInfo.InvariantCulture, ReceivedFormat, 2, 2));
        alice.Click(DeliveryButton(alice));
        await alice.WaitForTextAsync("Carol: Received by their app");
        Assert.True(alice.Shows("Bob: Received by their app"));
    }

    private static Button DeliveryButton(Device device) =>
        device.Named<ItemsControl>("MessageItems").GetVisualDescendants().OfType<Button>()
            .First(button => string.Equals(button.Name, "DeliveryStateButton", StringComparison.Ordinal) && button.IsEffectivelyVisible);

    private static CheckBox Candidate(Device device, string name) =>
        device.Named<ItemsControl>("CandidateItems").GetVisualDescendants().OfType<CheckBox>()
            .First(box => string.Equals(AutomationProperties.GetName(box), name, StringComparison.Ordinal));

    private static async Task OpenGroupAsync(Device device, string groupName)
    {
        await device.ClickUntilAsync(
            () => device.TryNamed<ItemsControl>("GroupItems")?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible && string.Equals(AutomationProperties.GetName(button), groupName, StringComparison.Ordinal)),
            () => string.Equals(device.Title, groupName, StringComparison.Ordinal),
            $"{device.Name} opens the group {groupName}");
    }
}
