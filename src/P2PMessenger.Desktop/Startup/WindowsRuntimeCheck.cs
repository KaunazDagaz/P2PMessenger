using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.Desktop.Startup;

public static partial class WindowsRuntimeCheck
{
    public const string LibraryName = "vcruntime140.dll";

    private const uint MessageBoxErrorStyle = 0x00000010u;

    public static bool EnsureAvailable() =>
        !OperatingSystem.IsWindows() || EnsureAvailable(IsLibraryLoadable, ShowErrorDialog);

    public static bool EnsureAvailable(Func<bool> isAvailable, Action<string, string> notifyMissing)
    {
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(notifyMissing);

        if (isAvailable())
        {
            return true;
        }

        notifyMissing(UiText.WindowsVcRuntimeMissingTitle, UiText.WindowsVcRuntimeMissingBody);
        return false;
    }

    public static bool IsLibraryLoadable()
    {
        if (!NativeLibrary.TryLoad(LibraryName, out nint handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void ShowErrorDialog(string title, string message) =>
        _ = MessageBoxW(0, message, title, MessageBoxErrorStyle);

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
