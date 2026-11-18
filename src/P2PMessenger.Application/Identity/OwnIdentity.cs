using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Identity;

public sealed class OwnIdentity : IDisposable
{
    public OwnIdentity(KeySet keys, DisplayName name)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(name);

        Keys = keys;
        Name = name;
        Code = ContactCode.Create(keys, name);
        CodeText = Code.ToText();
    }

    public KeySet Keys { get; }

    public DisplayName Name { get; }

    public ContactCode Code { get; }

    public string CodeText { get; }

    public Fingerprint Fingerprint => Keys.Fingerprint;

    public static OwnIdentity Create(DisplayName name) => new(KeySet.Create(), name);

    public void Dispose() => Keys.Dispose();
}
