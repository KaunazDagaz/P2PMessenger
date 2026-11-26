using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
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
