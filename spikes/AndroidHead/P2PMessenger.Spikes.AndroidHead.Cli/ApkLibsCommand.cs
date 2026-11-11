using System.Globalization;
using System.IO.Compression;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal static class ApkLibsCommand
{
    private const long SixteenKilobytes = 16 * 1024;

    public static int Run(CommandLine line)
    {
        var path = line.Positional(0);
        if (path is null || !File.Exists(path))
        {
            Console.WriteLine("apk-libs needs the path of an APK or AAB file, for example: apk-libs app.apk");
            return 2;
        }

        using var archive = ZipFile.OpenRead(path);
        var checkedFiles = 0;
        var failures = 0;
        foreach (var entry in archive.Entries.Where(IsLibrary).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            checkedFiles++;
            using var stream = entry.Open();
            using var memory = new MemoryStream((int)entry.Length);
            stream.CopyTo(memory);
            var stored = entry.CompressedLength == entry.Length ? "stored" : "compressed";
            if (!ElfLoadSegments.TryRead(memory.ToArray(), out var segments))
            {
                failures++;
                Console.WriteLine($"FAIL  {entry.FullName}: not a readable little-endian ELF file with LOAD segments");
                continue;
            }

            var smallest = segments.Alignments.Min();
            var passes = !segments.Is64Bit || smallest >= SixteenKilobytes;
            failures += passes ? 0 : 1;
            var alignments = string.Join(", ", segments.Alignments.Select(alignment => "0x" + alignment.ToString("x", CultureInfo.InvariantCulture)));
            var note = segments.Is64Bit ? string.Empty : " (32-bit; 16 KB pages exist only on 64-bit devices)";
            Console.WriteLine($"{(passes ? "PASS" : "FAIL")}  {entry.FullName}: {entry.Length} bytes, {stored}, {(segments.Is64Bit ? "64" : "32")}-bit, LOAD alignment {alignments}{note}");
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{checkedFiles} native libraries checked, {failures} not 16 KB-aligned."));
        return checkedFiles > 0 && failures == 0 ? 0 : 1;
    }

    private static bool IsLibrary(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("lib/", StringComparison.Ordinal) && entry.FullName.EndsWith(".so", StringComparison.Ordinal);
}
