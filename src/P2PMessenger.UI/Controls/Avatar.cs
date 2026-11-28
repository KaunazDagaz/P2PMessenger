using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace P2PMessenger.UI.Controls;

public sealed class Avatar : Control
{
    private static readonly IBrush[] Palette =
    [
        new ImmutableSolidColorBrush(Color.Parse("#2F5BEA")),
        new ImmutableSolidColorBrush(Color.Parse("#0F766E")),
        new ImmutableSolidColorBrush(Color.Parse("#B45309")),
        new ImmutableSolidColorBrush(Color.Parse("#7C3AED")),
        new ImmutableSolidColorBrush(Color.Parse("#BE185D")),
        new ImmutableSolidColorBrush(Color.Parse("#15803D")),
        new ImmutableSolidColorBrush(Color.Parse("#475569")),
    ];

    public static readonly StyledProperty<string?> DisplayNameProperty =
        AvaloniaProperty.Register<Avatar, string?>(nameof(DisplayName));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Avatar, double>(nameof(Size), 44);

    static Avatar()
    {
        AffectsRender<Avatar>(DisplayNameProperty, SizeProperty);
        AffectsMeasure<Avatar>(SizeProperty);
    }

    public string? DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static string InitialsOf(string? name)
    {
        string[] words = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        string first = FirstElement(words[0]);
        string initials = words.Length == 1 ? first : first + FirstElement(words[^1]);
        return initials.ToUpper(CultureInfo.CurrentCulture);
    }

    public static int PaletteIndexOf(string? name)
    {
        uint hash = 2166136261;
        foreach (char character in name ?? string.Empty)
        {
            hash = (hash ^ character) * 16777619;
        }

        return (int)(hash % (uint)Palette.Length);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        double side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(Palette[PaletteIndexOf(DisplayName)], null, center, side / 2, side / 2);

        string initials = InitialsOf(DisplayName);
        FormattedText text = new(
            initials,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            side * (initials.Length > 1 ? 0.36 : 0.42),
            Brushes.White);
        context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    private static string FirstElement(string word)
    {
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(word);
        return elements.MoveNext() ? elements.GetTextElement() : string.Empty;
    }
}
