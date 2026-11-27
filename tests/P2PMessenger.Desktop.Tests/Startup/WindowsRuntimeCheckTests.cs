using P2PMessenger.Desktop.Startup;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.Desktop.Tests.Startup;

public sealed class WindowsRuntimeCheckTests
{
    [Fact]
    public void EnsureAvailable_WhenLibraryLoads_ReturnsTrueAndDoesNotNotify()
    {
        bool notified = false;

        bool result = WindowsRuntimeCheck.EnsureAvailable(
            static () => true,
            (_, _) => notified = true);

        Assert.True(result);
        Assert.False(notified);
    }

    [Fact]
    public void EnsureAvailable_WhenLibraryMissing_NotifiesWithUiTextAndReturnsFalse()
    {
        string? capturedTitle = null;
        string? capturedBody = null;

        bool result = WindowsRuntimeCheck.EnsureAvailable(
            static () => false,
            (title, body) =>
            {
                capturedTitle = title;
                capturedBody = body;
            });

        Assert.False(result);
        Assert.Equal(UiText.WindowsVcRuntimeMissingTitle, capturedTitle);
        Assert.Equal(UiText.WindowsVcRuntimeMissingBody, capturedBody);
    }

    [Fact]
    public void IsLibraryLoadable_OnWindows_FindsVcRuntime()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "vcruntime140.dll is a Windows library.");

        Assert.True(WindowsRuntimeCheck.IsLibraryLoadable());
    }
}
