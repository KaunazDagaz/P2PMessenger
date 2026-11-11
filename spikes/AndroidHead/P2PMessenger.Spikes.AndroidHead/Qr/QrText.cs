using System.Text;

namespace P2PMessenger.Spikes.AndroidHead.Qr;

public static class QrText
{
    public static string Render(bool[,] modules, int quietModules, bool darkTerminal)
    {
        var size = modules.GetLength(0);
        var total = size + (2 * quietModules);
        var builder = new StringBuilder();
        for (var y = 0; y < total; y += 2)
        {
            for (var x = 0; x < total; x++)
            {
                var top = IsDark(modules, x - quietModules, y - quietModules);
                var bottom = IsDark(modules, x - quietModules, y + 1 - quietModules);
                builder.Append(darkTerminal ? Inverted(top, bottom) : Normal(top, bottom));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool IsDark(bool[,] modules, int x, int y)
    {
        var size = modules.GetLength(0);
        return x >= 0 && y >= 0 && x < size && y < size && modules[y, x];
    }

    private static char Normal(bool top, bool bottom) => (top, bottom) switch
    {
        (true, true) => '\u2588',
        (true, false) => '\u2580',
        (false, true) => '\u2584',
        _ => ' ',
    };

    private static char Inverted(bool top, bool bottom) => (top, bottom) switch
    {
        (true, true) => ' ',
        (true, false) => '\u2584',
        (false, true) => '\u2580',
        _ => '\u2588',
    };
}
