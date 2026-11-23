namespace P2PMessenger.Desktop.Camera;

public sealed record CameraEntry(int Index, string Kind, string Name, IReadOnlyList<CameraFormat> Formats);
