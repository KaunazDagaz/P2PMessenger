using P2PMessenger.Application.Internet;

namespace P2PMessenger.Android.Tor;

public static class AndroidTor
{
    public const string ExecutableName = "libTor.so";
    public const string DataFolderName = "tor";

    public static TorSetup? Setup(string? nativeLibraryFolder, string? privateFolder)
    {
        if (privateFolder is not { Length: > 0 } || !Path.IsPathFullyQualified(privateFolder))
        {
            return null;
        }

        string? executable = nativeLibraryFolder is { Length: > 0 } && Path.IsPathFullyQualified(nativeLibraryFolder)
            ? Path.Combine(nativeLibraryFolder, ExecutableName)
            : null;
        return TorSetup.For(executable, Path.Combine(privateFolder, DataFolderName));
    }
}
