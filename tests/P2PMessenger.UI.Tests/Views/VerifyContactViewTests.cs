using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class VerifyContactViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task Verify_TwoDevices_ShowTheSameTwoFingerprintsInTheSameOrder()
    {
        await using var alice = await UiHarness.StartAsync("Alice");
        await using var bob = await UiHarness.StartAsync("Bob");
        var bobForAlice = await AddCodeAsync(alice, bob);
        var aliceForBob = await AddCodeAsync(bob, alice);
        var atAlice = new VerifyContactView { DataContext = alice.Shell.Pages.VerifyContact(bobForAlice) };
        var atBob = new VerifyContactView { DataContext = bob.Shell.Pages.VerifyContact(aliceForBob) };
        alice.Show(atAlice);
        bob.Show(atBob);

        string[] onAlice = [Text(atAlice, "FirstFingerprint"), Text(atAlice, "SecondFingerprint")];
        string[] onBob = [Text(atBob, "FirstFingerprint"), Text(atBob, "SecondFingerprint")];

        Assert.Equal(onAlice, onBob);
        var pair = FingerprintPair.Create(alice.Messenger.Identity.Keys.IdentityKey, bob.Messenger.Identity.Keys.IdentityKey);
        Assert.Equal([pair.First.ToGroupedString(), pair.Second.ToGroupedString()], onAlice);
        Assert.Contains(alice.Messenger.Identity.Fingerprint.ToGroupedString(), onAlice);
        Assert.Contains(bob.Messenger.Identity.Fingerprint.ToGroupedString(), onAlice);
        string aliceFirstLabel = Text(atAlice, "FirstLabel");
        Assert.Equal(pair.OwnFirst ? UiText.VerifyYoursLabel : "Bob's", aliceFirstLabel);
        Assert.Equal(pair.OwnFirst ? "Alice's" : UiText.VerifyYoursLabel, Text(atBob, "FirstLabel"));
        Assert.Equal(6, onAlice[0].Split(' ').Length);
        alice.Messenger.Identity.Dispose();
        bob.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task Verify_TheyMatch_MarksKeyCheckedByYouAndNeverIdentityVerified()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.VerifyContact(contact);
        var view = new VerifyContactView { DataContext = page };
        harness.Show(view);
        Assert.StartsWith("Key not compared.", Text(view, "StatusText"), StringComparison.Ordinal);

        await page.MatchCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var saved));
        Assert.Equal(KeyComparison.CheckedByYou, saved.KeyComparison);
        Assert.StartsWith("Key checked by you.", Text(view, "StatusText"), StringComparison.Ordinal);
        Assert.False(UiHarness.Find<Button>(view, "MatchButton").IsEffectivelyVisible);
        Assert.True(UiHarness.Find<Button>(view, "UncheckButton").IsVisible);
        Assert.DoesNotContain(UiHarness.Texts(view), static text => text.Contains("verified", StringComparison.OrdinalIgnoreCase));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task Verify_TheyDoNotMatch_WarnsAndLeavesTheKeyNotCompared()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.VerifyContact(contact);
        var view = new VerifyContactView { DataContext = page };
        harness.Show(view);

        page.NoMatchCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(UiHarness.Find<TextBlock>(view, "MismatchText").IsVisible);
        Assert.Equal(UiText.VerifyMismatch, Text(view, "MismatchText"));
        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var saved));
        Assert.Equal(KeyComparison.NotCompared, saved.KeyComparison);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task Verify_MarkAsNotCompared_UndoesTheCheck()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var contact = await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.VerifyContact(contact);
        harness.Show(new VerifyContactView { DataContext = page });
        await page.MatchCommand.ExecuteAsync(null);

        await page.UncheckCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.False(page.IsChecked);
        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var saved));
        Assert.Equal(KeyComparison.NotCompared, saved.KeyComparison);
        harness.Messenger.Identity.Dispose();
    }

    private static string Text(Control view, string name) =>
        view.FindControl<TextBlock>(name)?.Text ?? throw new InvalidOperationException($"No text block named {name}.");

    private static async Task<Contact> AddCodeAsync(UiHarness at, UiHarness from)
    {
        var result = await at.Messenger.Contacts.AddAsync(at.Messenger.Contacts.Check(from.Messenger.Identity.CodeText), CancellationToken.None);
        Assert.True(result.IsSaved);
        return result.Contact;
    }
}
