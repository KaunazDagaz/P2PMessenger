namespace P2PMessenger.Desktop.Camera;

public sealed class CameraImage
{
    public CameraImage(int width, int height, byte[] bgra, byte[] luminance)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(luminance);
        if (width < 1 || height < 1 || bgra.Length != width * height * 4 || luminance.Length != width * height)
        {
            throw new ArgumentException("The buffers do not match the size.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
        Luminance = luminance;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }

    public byte[] Luminance { get; }
}
