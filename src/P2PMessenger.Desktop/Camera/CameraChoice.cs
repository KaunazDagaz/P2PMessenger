namespace P2PMessenger.Desktop.Camera;

public static class CameraChoice
{
    public const int PreferredWidth = 640;
    public const string DirectShow = "DirectShow";
    public const string VideoForWindows = "VideoForWindows";
    public const string V4L2 = "V4L2";
    public const string DefaultEntryName = "Default";

    public static IReadOnlyList<CameraEntry> Order(IEnumerable<CameraEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries
            .Where(static entry => !IsDefaultVideoForWindows(entry) && ChooseFormat(entry.Formats) is not null)
            .OrderBy(static entry => Rank(entry.Kind))
            .ThenBy(static entry => entry.Index)];
    }

    public static CameraFormat? ChooseFormat(IReadOnlyList<CameraFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        CameraFormat[] usable = [.. formats.Where(static format => format.IsKnown && format.Width > 0 && format.Height > 0)];
        return Best(usable.Where(static format => !format.IsCompressed)) ?? Best(usable);
    }

    private static CameraFormat? Best(IEnumerable<CameraFormat> formats) =>
        formats
            .OrderBy(static format => Math.Abs(format.Width - PreferredWidth))
            .ThenByDescending(static format => format.FramesPerSecond)
            .ThenBy(static format => format.Index)
            .FirstOrDefault();

    private static bool IsDefaultVideoForWindows(CameraEntry entry) =>
        string.Equals(entry.Kind, VideoForWindows, StringComparison.Ordinal)
        && string.Equals(entry.Name.Trim(), DefaultEntryName, StringComparison.OrdinalIgnoreCase);

    private static int Rank(string kind) => kind switch
    {
        DirectShow or V4L2 => 0,
        VideoForWindows => 2,
        _ => 1,
    };
}
