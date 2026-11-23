using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Desktop.Camera;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.Desktop.Tests.Camera;

public sealed class CameraScanSessionTests
{
    [Fact]
    [Trait("Story", "US-03")]
    public async Task StartAsync_FirstEntryFailsToOpen_OpensTheNextAndReadsTheCodeOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = CodeText("Bob");
        var cameras = new FakeCameras(failing: 1);
        CameraEntry[] entries = [Entry(0, "DirectShow"), Entry(1, "MediaFoundation")];
        await using var session = new CameraScanSession(cameras, static _ => null);
        var shown = 0;
        session.FrameShown += (_, _) => Interlocked.Increment(ref shown);

        Assert.Equal(CameraOpenOutcome.Started, await session.StartAsync(entries, cancellationToken));
        Assert.Equal([0, 1], cameras.Opened);
        cameras.Send(Frame(QrBitmap(text)));
        cameras.Send(Frame(QrBitmap(CodeText("Carol"))));

        Assert.Equal(text, await session.Found.WaitAsync(cancellationToken));
        Assert.Equal(1, shown);
        Assert.Equal(1, session.FramesRead);
    }

    [Fact]
    [Trait("Story", "US-03")]
    public async Task StartAsync_NoEntryOpens_IsNoCamera()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cameras = new FakeCameras(failing: 2);
        await using var session = new CameraScanSession(cameras, static _ => null);

        Assert.Equal(CameraOpenOutcome.NoCamera, await session.StartAsync([Entry(0, "DirectShow"), Entry(1, "V4L2")], cancellationToken));
        Assert.Equal(CameraOpenOutcome.NoCamera, await session.StartAsync([Entry(0, "VideoForWindows", "Default")], cancellationToken));
        Assert.Equal(CameraOpenOutcome.NoCamera, await session.StartAsync([], cancellationToken));
        Assert.False(session.Found.IsCompleted);
    }

    [Fact]
    public async Task OnFrame_UnreadableAndBlankFrames_AreSkippedUntilACodeComes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = CodeText("Dave");
        var cameras = new FakeCameras(failing: 0);
        var jpegs = 0;
        await using var session = new CameraScanSession(cameras, _ =>
        {
            Interlocked.Increment(ref jpegs);
            return null;
        });
        Assert.Equal(CameraOpenOutcome.Started, await session.StartAsync([Entry(0, "V4L2")], cancellationToken));

        cameras.Send([1, 2, 3]);
        cameras.Send([0xFF, 0xD8, 0xFF, 0xE0]);
        cameras.Send(Frame(CameraFramesTests.Bitmap(320, 240, 24, true, static (_, _) => (128, 128, 128))));
        Assert.False(session.Found.IsCompleted);
        cameras.Send(Frame(QrBitmap(text)));

        Assert.Equal(text, await session.Found.WaitAsync(cancellationToken));
        Assert.Equal(1, jpegs);
        Assert.Equal(2, session.FramesRead);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheCameraAndIgnoresLaterFrames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cameras = new FakeCameras(failing: 0);
        var session = new CameraScanSession(cameras, static _ => null);
        Assert.Equal(CameraOpenOutcome.Started, await session.StartAsync([Entry(0, "V4L2")], cancellationToken));

        await session.DisposeAsync();
        await session.DisposeAsync();
        cameras.Send(Frame(QrBitmap(CodeText("Erin"))));

        Assert.Equal(1, cameras.Stopped);
        Assert.False(session.Found.IsCompleted);
        Assert.Equal(0, session.FramesRead);
    }

    private static byte[] Frame(byte[] bitmap) => bitmap;

    private static byte[] QrBitmap(string text)
    {
        var pixels = QrMatrix.Encode(text).ToLuminance(3, out var width);
        var side = 400;
        var offset = (side - width) / 2;
        return CameraFramesTests.Bitmap(side, side, 24, true, (x, y) =>
        {
            var inside = x >= offset && y >= offset && x < offset + width && y < offset + width;
            var value = inside ? pixels[((y - offset) * width) + (x - offset)] : (byte)255;
            return (value, value, value);
        });
    }

    private static string CodeText(string name)
    {
        using var keys = KeySet.Create();
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        return ContactCode.Create(keys, displayName).ToText();
    }

    private static CameraEntry Entry(int index, string kind, string name = "Webcam") =>
        new(index, kind, name, [new CameraFormat(0, 400, 400, 30, false, true)]);

    private sealed class FakeCameras(int failing) : ICameraSource
    {
        private Action<byte[]>? onFrame;

        public List<int> Opened { get; } = [];

        public int Stopped { get; private set; }

        public IReadOnlyList<CameraEntry> List() => [];

        public Task<IAsyncDisposable> OpenAsync(CameraEntry entry, CameraFormat format, Action<byte[]> onFrame, CancellationToken cancellationToken)
        {
            Opened.Add(entry.Index);
            if (Opened.Count <= failing)
            {
                throw new InvalidOperationException("Couldn't set video format.");
            }

            this.onFrame = onFrame;
            return Task.FromResult<IAsyncDisposable>(new Stopper(this));
        }

        public void Send(byte[] image) => onFrame?.Invoke(image);

        private sealed class Stopper(FakeCameras owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Stopped++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
