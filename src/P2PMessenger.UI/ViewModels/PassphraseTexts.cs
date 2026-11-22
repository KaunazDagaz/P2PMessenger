using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class PassphraseTexts
{
    public static string Hint => Formats.With(Formats.PassphraseHint, PassphrasePolicy.MinimumLength);

    public static string? ProblemWith(string passphrase, string repeated) => PassphrasePolicy.Check(passphrase) switch
    {
        PassphraseProblem.TooShort => Formats.With(Formats.PassphraseTooShort, PassphrasePolicy.MinimumLength),
        PassphraseProblem.NotText => UiText.PassphraseNotText,
        _ => string.Equals(passphrase, repeated, StringComparison.Ordinal) ? null : UiText.PassphraseMismatch,
    };
}
