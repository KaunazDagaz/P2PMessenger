using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using P2PMessenger.UI.Qr;

namespace P2PMessenger.UI.Controls;

public sealed class QrCodeView : Control
{
    public const double DefaultSide = 280;

    public static readonly StyledProperty<QrMatrix?> MatrixProperty =
        AvaloniaProperty.Register<QrCodeView, QrMatrix?>(nameof(Matrix));

    static QrCodeView()
    {
        AffectsRender<QrCodeView>(MatrixProperty);
        AffectsMeasure<QrCodeView>(MatrixProperty);
    }

    public QrMatrix? Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Matrix is not { } matrix)
        {
            return;
        }

        double side = Math.Min(Bounds.Width, Bounds.Height);
        int cells = matrix.SizeWithQuietZone;
        if (side <= 0)
        {
            return;
        }

        double module = side / cells >= 1 ? Math.Floor(side / cells) : side / cells;
        double total = module * cells;
        double left = Math.Floor((Bounds.Width - total) / 2);
        double top = Math.Floor((Bounds.Height - total) / 2);
        context.FillRectangle(Brushes.White, new Rect(left, top, total, total));

        for (int y = 0; y < matrix.Size; y++)
        {
            int x = 0;
            while (x < matrix.Size)
            {
                if (!matrix[x, y])
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < matrix.Size && matrix[x, y])
                {
                    x++;
                }

                context.FillRectangle(
                    Brushes.Black,
                    new Rect(
                        left + ((start + QrMatrix.QuietZone) * module),
                        top + ((y + QrMatrix.QuietZone) * module),
                        (x - start) * module,
                        module));
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Matrix is null)
        {
            return default;
        }

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : DefaultSide;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : DefaultSide;
        double side = Math.Min(DefaultSide, Math.Min(width, height));
        return new Size(side, side);
    }
}
