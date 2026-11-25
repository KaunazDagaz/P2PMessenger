using System.Globalization;
using System.Text;
using Avalonia.Controls;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Support;

internal static class Flows
{
    public const string Passphrase = "lantern orchard pebble";

    private static readonly CompositeFormat AddedFormat = CompositeFormat.Parse(UiText.ContactAddedNoticeFormat);

    private static readonly CompositeFormat ChatDeletedFormat = CompositeFormat.Parse(UiText.ConversationChatDeletedFormat);

    public static string AddedNotice(string name) => string.Format(CultureInfo.InvariantCulture, AddedFormat, name);

    public static string ChatDeletedNotice(string name) => string.Format(CultureInfo.InvariantCulture, ChatDeletedFormat, name);

    public static async Task CreateIdentityAsync(Device device, string? passphrase = null)
    {
        await device.WaitForTitleAsync(UiText.CreateIdentityTitle);
        device.Type("NameBox", device.Name);
        if (passphrase is not null)
        {
            device.Type("PassphraseBox", passphrase);
            device.Type("RepeatBox", passphrase);
        }

        device.ClickButton("CreateButton");
        await device.WaitForTitleAsync(UiText.ContactListTitle);
    }

    public static async Task<string> ReadOwnCodeAsync(Device device)
    {
        device.ClickButton("MyCodeButton");
        await device.WaitForTitleAsync(UiText.ProfileTitle);
        string code = device.Named<TextBox>("CodeBox").Text ?? string.Empty;
        Assert.StartsWith("P2PM1-", code, StringComparison.Ordinal);
        device.ClickButton("BackButton");
        await device.WaitForTitleAsync(UiText.ContactListTitle);
        return code;
    }

    public static async Task AddContactAsync(Device device, string code, string contactName)
    {
        device.ClickButton("AddContactButton");
        await device.WaitForTitleAsync(UiText.AddContactTitle);
        device.Type("CodeBox", code);
        device.ClickButton("CheckButton");
        await device.WaitUntilAsync(() => device.TryNamed<Border>("PreviewPanel") is not null, $"{device.Name} shows the preview of the code from {contactName}");
        Assert.Equal(contactName, device.Named<TextBlock>("PreviewName").Text);
        device.ClickButton("AddButton");
        await device.WaitForTitleAsync(contactName);
    }

    public static async Task AddEachOtherAsync(Device first, Device second)
    {
        string firstCode = await ReadOwnCodeAsync(first);
        string secondCode = await ReadOwnCodeAsync(second);
        await AddContactAsync(first, secondCode, second.Name);
        await AddContactAsync(second, firstCode, first.Name);
    }

    public static async Task WaitUntilConnectedAsync(Device first, Device second)
    {
        await first.WaitForTextAsync(UiText.ContactReachableNearby);
        await second.WaitForTextAsync(UiText.ContactReachableNearby);
    }

    public static async Task SetUpPairAsync(Device first, Device second, string? firstPassphrase = null)
    {
        await first.LaunchAsync();
        await second.LaunchAsync();
        await CreateIdentityAsync(first, firstPassphrase);
        await CreateIdentityAsync(second);
        await AddEachOtherAsync(first, second);
        await WaitUntilConnectedAsync(first, second);
    }

    public static async Task SendAsync(Device device, string text)
    {
        device.Type("DraftBox", text);
        device.ClickButton("SendButton");
        await device.WaitUntilAsync(
            () => device.Shows(text) && string.IsNullOrEmpty(device.Named<TextBox>("DraftBox").Text),
            $"{device.Name} shows the sent message and an empty message box");
    }

    public static async Task OpenConversationAsync(Device device, string contactName)
    {
        await device.WaitUntilAsync(() => device.ContactButton(contactName) is not null, $"{device.Name} lists {contactName} among the contacts");
        await device.ClickUntilAsync(
            () => device.ContactButton(contactName),
            () => string.Equals(device.Title, contactName, StringComparison.Ordinal),
            $"{device.Name} opens the conversation with {contactName}");
    }

    public static async Task GoBackToContactsAsync(Device device)
    {
        device.ClickButton("BackButton");
        await device.WaitForTitleAsync(UiText.ContactListTitle);
    }
}
