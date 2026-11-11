using System.Runtime.InteropServices;

namespace P2PMessenger.Spikes.Tor.Cli;

internal static class SpikePaths
{
    private const string MarkerFile = "get-tor.ps1";

    public static string StateDirectory(SpikeOptions options)
    {
        var path = options.StateDirectory ?? Path.Combine(FindSpikeRoot() ?? Environment.CurrentDirectory, "run");
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    public static string TorExecutable(SpikeOptions options) =>
        options.TorExecutable ?? FindBundledTor() ?? "tor";

    private static string? FindSpikeRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, MarkerFile)))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    private static string? FindBundledTor()
    {
        var root = FindSpikeRoot();
        if (root is null)
        {
            return null;
        }

        var bundles = Path.Combine(root, "tor-bundles");
        if (!Directory.Exists(bundles))
        {
            return null;
        }

        var (platform, executable) = CurrentPlatform();
        if (platform is null)
        {
            return null;
        }

        return Directory.EnumerateDirectories(bundles)
            .Select(folder => (Folder: folder, Version: Version.TryParse(Path.GetFileName(folder), out var version) ? version : null))
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(item.Folder, platform, "tor", executable))
            .FirstOrDefault(File.Exists);
    }

    private static (string? Platform, string Executable) CurrentPlatform()
    {
        var architecture = RuntimeInformation.OSArchitecture;
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64)
        {
            return ("windows-x86_64", "tor.exe");
        }

        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => ("linux-x86_64", "tor"),
                Architecture.Arm64 => ("linux-aarch64", "tor"),
                _ => (null, "tor"),
            };
        }

        return (null, "tor");
    }
}
