using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace P2PMessenger.Storage.Protection;

public static class PassphraseText
{
    public static bool TryNormalize(string passphrase, [NotNullWhen(true)] out string? normalized)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        normalized = null;
        if (!IsWellFormed(passphrase))
        {
            return false;
        }

        try
        {
            normalized = passphrase.Normalize(NormalizationForm.FormKC);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static int CountCharacters(string passphrase)
    {
        if (!TryNormalize(passphrase, out string? normalized))
        {
            return -1;
        }

        int count = 0;
        foreach (Rune unused in normalized.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    public static bool IsWellFormed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (char.IsHighSurrogate(current) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(current))
            {
                return false;
            }
        }

        return true;
    }

    public static byte[] Encode(string passphrase)
    {
        if (!TryNormalize(passphrase, out string? normalized))
        {
            throw new ArgumentException("The passphrase is not valid Unicode text.", nameof(passphrase));
        }

        return Encoding.UTF8.GetBytes(normalized);
    }
}
