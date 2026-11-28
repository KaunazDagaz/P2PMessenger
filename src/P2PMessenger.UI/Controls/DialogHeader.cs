using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace P2PMessenger.UI.Controls;

public sealed class DialogHeader : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> GlyphProperty =
        AvaloniaProperty.Register<DialogHeader, Geometry?>(nameof(Glyph));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Title));

    public Geometry? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
