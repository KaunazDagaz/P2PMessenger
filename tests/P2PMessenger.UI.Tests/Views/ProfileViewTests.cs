using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.UI.Controls;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class ProfileViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task Profile_ShowsNameFingerprintAndTheTextCode()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new ProfileView { DataContext = harness.Shell.Pages.Profile() };

        harness.Show(view);

        var identity = harness.Messenger.Identity;
        Assert.Equal("Alice", UiHarness.Find<TextBlock>(view, "NameText").Text);
        Assert.Equal(identity.Fingerprint.ToGroupedString(), UiHarness.Find<TextBlock>(view, "FingerprintText").Text);
        Assert.Equal(identity.CodeText, UiHarness.Find<TextBox>(view, "CodeBox").Text);
        Assert.StartsWith("P2PM1-", identity.CodeText, StringComparison.Ordinal);
        identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task Profile_ShowsTheQrCodeOfTheTextCode()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Profile();
        var view = new ProfileView { DataContext = page };

        harness.Show(view);

        var shown = UiHarness.Find<QrCodeView>(view, "QrCode");
        Assert.Same(page.QrCode, shown.Matrix);
        Assert.True(shown.Bounds.Width > 0);
        Assert.Equal(shown.Bounds.Width, shown.Bounds.Height);
        Assert.Equal(harness.Messenger.Identity.CodeText, AddInPersonViewTests.Decode(page.QrCode));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task Profile_SaveQrImage_HandsThePngToThePlatform()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Profile();
        var view = new ProfileView { DataContext = page };
        harness.Show(view);

        await page.SaveImageCommand.ExecuteAsync(null);
        UiHarness.Flush();

        var saved = Assert.NotNull(harness.Platform.SavedImage);
        Assert.Equal("Alice contact code.png", saved.Name);
        Assert.Equal(QrPng.Render(page.QrCode), saved.Png);
        Assert.Equal(UiText.ProfileImageSaved, UiHarness.Find<TextBlock>(view, "StatusText").Text);

        harness.Platform.SaveResult = new CodeFileResult(CodeFileStatus.Failed, null);
        await page.SaveImageCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Equal(UiText.ProfileSaveFailed, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(UiHarness.Find<TextBlock>(view, "StatusText").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-03")]
    public async Task Profile_CopyAndSave_HandTheCodeToThePlatform()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Profile();
        var view = new ProfileView { DataContext = page };
        harness.Show(view);

        await page.CopyCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Equal(harness.Messenger.Identity.CodeText, harness.Platform.Clipboard);
        Assert.Equal(UiText.ProfileCopied, UiHarness.Find<TextBlock>(view, "StatusText").Text);

        await page.SaveCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Equal(("Alice contact code.txt", harness.Messenger.Identity.CodeText), harness.Platform.Saved);
        Assert.Equal(UiText.ProfileSaved, UiHarness.Find<TextBlock>(view, "StatusText").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Profile_PlatformFails_ShowsTheErrors()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Profile();
        var view = new ProfileView { DataContext = page };
        harness.Show(view);
        harness.Platform.ClipboardWorks = false;
        harness.Platform.SaveResult = new CodeFileResult(CodeFileStatus.Failed, null);

        await page.CopyCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Equal(UiText.ProfileCopyFailed, UiHarness.Find<TextBlock>(view, "ErrorText").Text);

        await page.SaveCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.Equal(UiText.ProfileSaveFailed, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(UiHarness.Find<TextBlock>(view, "StatusText").IsVisible);
        harness.Messenger.Identity.Dispose();
    }
}
