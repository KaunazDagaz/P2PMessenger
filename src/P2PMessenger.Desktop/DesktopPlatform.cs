using System.Globalization;
using P2PMessenger.Application.Background;
using P2PMessenger.Application.Internet;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Desktop.Camera;
using P2PMessenger.Desktop.KeyStores;
using P2PMessenger.Desktop.Settings;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Desktop;

public static class DesktopPlatform
{
    public const string DataOption = "--data";
    public const string FolderName = "P2PMessenger";
    public const string TorFolderName = "tor";

    public static AppPlatform FromArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? dataFolder = DataFolder(arguments);
        return new AppPlatform(dataFolder, CreateKeyProtector(), null, null, new DesktopQrScanner())
        {
            Tor = Tor(dataFolder, AppContext.BaseDirectory),
            Settings = dataFolder is { Length: > 0 } ? new FileSettingsStore(dataFolder) : new InMemorySettingsStore(),
            CanReceivePcCopy = true,
        };
    }

    public static TorSetup Tor(string? dataFolder, string applicationFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationFolder);
        string executable = Path.Combine(Path.GetFullPath(applicationFolder), TorFolderName, OperatingSystem.IsWindows() ? "tor.exe" : "tor");
        string torData = dataFolder is { Length: > 0 }
            ? Path.Combine(dataFolder, TorFolderName)
            : Path.Combine(Path.GetTempPath(), FolderName + "-tor-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return TorSetup.For(executable, torData);
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
