namespace P2PMessenger.Core.Text;

internal static class Base32
{
    private const string UpperAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string LowerAlphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static int GetEncodedLength(int byteCount) => ((byteCount * 8) + 4) / 5;

    public static string Encode(ReadOnlySpan<byte> data, bool lowerCase)
    {
        string alphabet = lowerCase ? LowerAlphabet : UpperAlphabet;
        char[] chars = new char[GetEncodedLength(data.Length)];
        int count = 0;
        int buffer = 0;
        int bits = 0;

        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                chars[count++] = alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }

            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            chars[count] = alphabet[(buffer << (5 - bits)) & 31];
        }

        return new string(chars);
    }

    public static bool TryDecode(ReadOnlySpan<char> text, Span<byte> destination, out int written)
    {
        written = 0;

        if ((text.Length % 8) is 1 or 3 or 6 || destination.Length < (text.Length * 5) / 8)
        {
            return false;
        }

        int count = 0;
        int buffer = 0;
        int bits = 0;

        foreach (char c in text)
        {
            int value = ValueOf(c);
            if (value < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                destination[count++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }

        if (buffer != 0)
        {
            return false;
        }

        written = count;
        return true;
    }

    private static int ValueOf(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        >= '2' and <= '7' => c - '2' + 26,
        _ => -1,
    };
}
