using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using NSec.Cryptography;
using NoiseNet = Noise;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class LibsodiumChecks
{
    private const string LibraryName = "libsodium";
    private const string ProcessMaps = "/proc/self/maps";

    public static void Run(CheckReport report)
    {
        report.Run("NSec and Noise.NET resolve the same libsodium", SameLibrary);
        report.Run("libsodium files mapped into this process", LoadedFiles);
    }

    private static CheckOutcome SameLibrary()
    {
        Assembly nsec = typeof(Key).Assembly;
        Assembly noise = typeof(NoiseNet.Protocol).Assembly;
        if (!NativeLibrary.TryLoad(LibraryName, nsec, null, out IntPtr nsecHandle))
        {
            return CheckOutcome.Fail("libsodium cannot be loaded the way NSec loads it");
        }

        try
        {
            if (!NativeLibrary.TryLoad(LibraryName, noise, null, out IntPtr noiseHandle))
            {
                return CheckOutcome.Fail("libsodium cannot be loaded the way Noise.NET loads it");
            }

            NativeLibrary.Free(noiseHandle);
            return nsecHandle == noiseHandle
                ? CheckOutcome.Pass("both get the same library handle")
                : CheckOutcome.Fail("NSec and Noise.NET get different libraries");
        }
        finally
        {
            NativeLibrary.Free(nsecHandle);
        }
    }

    private static CheckOutcome LoadedFiles()
    {
        List<string> files = OperatingSystem.IsWindows() ? WindowsModules() : MappedFiles();
        return files.Count switch
        {
            1 => CheckOutcome.Pass("one file: " + files[0]),
            0 => CheckOutcome.Info("no mapped file has \"sodium\" in its name; on Android the library may be mapped straight from the APK"),
            _ => CheckOutcome.Fail("more than one file: " + string.Join("; ", files)),
        };
    }

    private static List<string> WindowsModules()
    {
        using Process process = Process.GetCurrentProcess();
        List<string> files = [];
        foreach (ProcessModule module in process.Modules)
        {
            using (module)
            {
                if (module.FileName.Contains("sodium", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(module.FileName);
                }
            }
        }

        return [.. files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static List<string> MappedFiles()
    {
        if (!File.Exists(ProcessMaps))
        {
            return [];
        }

        return [.. File.ReadLines(ProcessMaps)
            .Select(PathOf)
            .Where(path => path.Contains("sodium", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static string PathOf(string line)
    {
        string[] fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 6 ? fields[5].Trim() : string.Empty;
    }
}
