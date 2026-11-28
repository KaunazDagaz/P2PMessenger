using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace P2PMessenger.UI.Controls;

public static class Icons
{
    public static readonly AttachedProperty<Geometry?> GlyphProperty =
        AvaloniaProperty.RegisterAttached<Button, Geometry?>("Glyph", typeof(Icons));

    public static Geometry? GetGlyph(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.GetValue(GlyphProperty);
    }

    public static void SetGlyph(Button button, Geometry? value)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(GlyphProperty, value);
    }
}
