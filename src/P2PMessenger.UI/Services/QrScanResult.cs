using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.UI.Services;

public readonly record struct QrScanResult(QrScanStatus Status, string? Text)
{
    public static QrScanResult Cancelled => new(QrScanStatus.Cancelled, null);

    public static QrScanResult NoCamera => new(QrScanStatus.NoCamera, null);

    public static QrScanResult CameraRefused => new(QrScanStatus.CameraRefused, null);

    public static QrScanResult Failed => new(QrScanStatus.Failed, null);

    [MemberNotNullWhen(true, nameof(Text))]
    public bool IsRead => Status == QrScanStatus.Done && Text is not null;

    public static QrScanResult Read(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new QrScanResult(QrScanStatus.Done, text);
    }
}
