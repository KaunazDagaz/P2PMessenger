using Avalonia;
using Avalonia.Controls;

namespace P2PMessenger.UI.Controls;

public sealed class FormField : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Label));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
