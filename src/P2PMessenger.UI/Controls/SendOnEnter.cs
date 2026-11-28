using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace P2PMessenger.UI.Controls;

public static class SendOnEnter
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("Command", typeof(SendOnEnter));

    static SendOnEnter()
    {
        CommandProperty.Changed.AddClassHandler<TextBox>(static (box, change) => OnCommandChanged(box, change.NewValue is not null));
    }

    public static bool IsEnterSendKey => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    public static ICommand? GetCommand(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return box.GetValue(CommandProperty);
    }

    public static void SetCommand(TextBox box, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(box);
        box.SetValue(CommandProperty, value);
    }

    private static void OnCommandChanged(TextBox box, bool attached)
    {
        box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (attached)
        {
            box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0 || !IsEnterSendKey)
        {
            return;
        }

        if (GetCommand(box) is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
        }
    }
}
