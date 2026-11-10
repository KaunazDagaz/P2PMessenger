using System.Globalization;

namespace P2PMessenger.Spikes.Noise.Cli;

internal static class AndroidLibraryCheck
{
    private const long SixteenKilobytes = 16 * 1024;

    public static int Run(string? packagesFolder)
    {
        string root = packagesFolder
            ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string package = Path.Combine(root, "libsodium");
        Console.WriteLine("Android builds of libsodium in " + package);
        if (!Directory.Exists(package))
        {
            Console.WriteLine("The libsodium package is not there; build the spike first, so that NuGet downloads it.");
            return 1;
        }

        int checkedFiles = 0;
        int failures = 0;
        foreach (string file in Directory.EnumerateFiles(package, "*.so", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(package, file);
            if (!relative.Contains("android-", StringComparison.Ordinal))
            {
                continue;
            }

            checkedFiles++;
            if (!ElfLoadSegments.TryRead(File.ReadAllBytes(file), out ElfLoadSegments? segments))
            {
                failures++;
                Console.WriteLine($"FAIL  {relative}: not a readable little-endian ELF file with LOAD segments");
                continue;
            }

            long smallest = segments.Alignments.Min();
            bool passes = !segments.Is64Bit || smallest >= SixteenKilobytes;
            failures += passes ? 0 : 1;
            string alignments = string.Join(", ", segments.Alignments.Select(alignment => "0x" + alignment.ToString("x", CultureInfo.InvariantCulture)));
            string note = segments.Is64Bit ? string.Empty : " (32-bit; 16 KB pages exist only on 64-bit devices)";
            Console.WriteLine($"{(passes ? "PASS" : "FAIL")}  {relative}: {(segments.Is64Bit ? "64" : "32")}-bit, LOAD alignment {alignments}{note}");
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{checkedFiles} files checked, {failures} not 16 KB-aligned."));
        return checkedFiles > 0 && failures == 0 ? 0 : 1;
    }
}
