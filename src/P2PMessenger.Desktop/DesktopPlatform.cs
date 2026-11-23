using P2PMessenger.Application.LocalData;
using P2PMessenger.Desktop.Camera;
using P2PMessenger.Desktop.KeyStores;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Desktop;

public static class DesktopPlatform
{
    public const string DataOption = "--data";
    public const string FolderName = "P2PMessenger";

    public static AppPlatform FromArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new AppPlatform(DataFolder(arguments), CreateKeyProtector(), null, null, new DesktopQrScanner());
    }

    public static string? DataFolder(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, DataOption, StringComparison.Ordinal) && index + 1 < arguments.Count && !string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return Path.GetFullPath(arguments[index + 1]);
            }

            if (argument.StartsWith(DataOption + "=", StringComparison.Ordinal) && argument.Length > DataOption.Length + 1)
            {
                return Path.GetFullPath(argument[(DataOption.Length + 1)..]);
            }
        }

        return DefaultDataFolder();
    }

    public static string? DefaultDataFolder()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(local) ? null : Path.Combine(local, FolderName);
    }

    public static IKeyProtector? CreateKeyProtector()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiKeyProtector();
        }

        if (OperatingSystem.IsLinux())
        {
            return new SecretServiceKeyProtector();
        }

        return null;
    }
}
