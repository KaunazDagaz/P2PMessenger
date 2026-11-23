using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Contacts;

public sealed class Contact
{
    public Contact(ContactCode code, ContactState state, KeyComparison keyComparison)
        : this(code, state, keyComparison, CodeExchange.OtherChannel)
    {
    }

    public Contact(ContactCode code, ContactState state, KeyComparison keyComparison, CodeExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(code);

        Code = code;
        State = state;
        KeyComparison = keyComparison;
        Exchange = exchange;
    }

    public ContactCode Code { get; }

    public ContactState State { get; }

    public KeyComparison KeyComparison { get; }

    public CodeExchange Exchange { get; }

    public Ed25519PublicKey IdentityKey => Code.IdentityKey;

    public DisplayName Name => Code.DisplayName;

    public Fingerprint Fingerprint => Code.Fingerprint;

    public bool IsAdded => State == ContactState.Added;

    internal Contact WithState(ContactState state) => new(Code, state, KeyComparison, Exchange);

    internal Contact WithKeyComparison(KeyComparison keyComparison) => new(Code, State, keyComparison, Exchange);

    internal Contact WithNewConnectionKeys(ContactCode code) => new(code, ContactState.WaitingForFirstConnection, KeyComparison, Exchange);

    internal static Contact ScannedInPerson(ContactCode code) => new(code, ContactState.WaitingForFirstConnection, KeyComparison.CheckedByYou, CodeExchange.InPerson);
}
