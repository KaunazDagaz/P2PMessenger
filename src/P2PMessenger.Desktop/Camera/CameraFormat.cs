namespace P2PMessenger.Desktop.Camera;

public sealed record CameraFormat(int Index, int Width, int Height, double FramesPerSecond, bool IsCompressed, bool IsKnown);
