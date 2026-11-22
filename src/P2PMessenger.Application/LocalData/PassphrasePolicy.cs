using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Application.LocalData;

public static class PassphrasePolicy
{
    public const int MinimumLength = 12;

    public static PassphraseProblem Check(string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return PassphraseProblem.TooShort;
        }

        int length = PassphraseText.CountCharacters(passphrase);
        if (length < 0)
        {
            return PassphraseProblem.NotText;
        }

        return length < MinimumLength ? PassphraseProblem.TooShort : PassphraseProblem.None;
    }
}
