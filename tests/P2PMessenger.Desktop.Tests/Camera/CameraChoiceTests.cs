using P2PMessenger.Desktop.Camera;

namespace P2PMessenger.Desktop.Tests.Camera;

public sealed class CameraChoiceTests
{
    [Fact]
    [Trait("Story", "US-03")]
    public void Order_TheSameWebcamThreeTimes_TriesDirectShowFirstAndSkipsTheDefaultEntry()
    {
        CameraEntry[] entries =
        [
            Entry(0, "VideoForWindows", "Default", Yuyv(640, 480)),
            Entry(1, "DirectShow", "Integrated Webcam", Yuyv(640, 480)),
            Entry(2, "MediaFoundation", "Integrated Webcam", Format(0, 640, 480, 30, false)),
        ];

        var ordered = CameraChoice.Order(entries);

        Assert.Equal([1, 2], ordered.Select(static entry => entry.Index));
    }

    [Fact]
    [Trait("Story", "US-03")]
    public void Order_VideoForWindowsCameraWithItsOwnName_IsKeptAsTheLastResort()
    {
        CameraEntry[] entries =
        [
            Entry(0, "VideoForWindows", "USB Camera", Yuyv(640, 480)),
            Entry(1, "V4L2", "UVC Camera", Yuyv(640, 480)),
        ];

        Assert.Equal([1, 0], CameraChoice.Order(entries).Select(static entry => entry.Index));
    }

    [Fact]
    [Trait("Story", "US-03")]
    public void Order_NoEntryOrOnlyUnusableEntries_IsEmpty()
    {
        Assert.Empty(CameraChoice.Order([]));
        Assert.Empty(CameraChoice.Order([Entry(0, "VideoForWindows", "Default", Yuyv(640, 480))]));
        Assert.Empty(CameraChoice.Order([Entry(0, "V4L2", "Capture card", Format(0, 640, 480, 30, false, false))]));
        Assert.Empty(CameraChoice.Order([Entry(0, "DirectShow", "Empty")]));
    }

    [Fact]
    public void ChooseFormat_PrefersAnUncompressedFormatNear640WideWithTheMostFrames()
    {
        CameraFormat[] formats =
        [
            Format(0, 1920, 1080, 30, false),
            Format(1, 640, 480, 15, false),
            Format(2, 640, 480, 30, true),
            Format(3, 640, 480, 30, false),
            Format(4, 320, 240, 30, false),
            Format(5, 640, 480, 60, false, false),
        ];

        Assert.Equal(3, CameraChoice.ChooseFormat(formats)?.Index);
    }

    [Fact]
    public void ChooseFormat_OnlyCompressedFormats_TakesTheCompressedOneNear640Wide()
    {
        CameraFormat[] formats =
        [
            Format(0, 1280, 720, 30, true),
            Format(1, 800, 600, 30, true),
        ];

        Assert.Equal(1, CameraChoice.ChooseFormat(formats)?.Index);
        Assert.Null(CameraChoice.ChooseFormat([Format(0, 0, 0, 30, false)]));
    }

    private static CameraFormat[] Yuyv(int width, int height) => [Format(0, width, height, 30, false)];

    private static CameraFormat Format(int index, int width, int height, double framesPerSecond, bool compressed, bool known = true) =>
        new(index, width, height, framesPerSecond, compressed, known);

    private static CameraEntry Entry(int index, string kind, string name, params CameraFormat[] formats) => new(index, kind, name, formats);
}
