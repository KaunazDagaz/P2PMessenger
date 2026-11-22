using System.Net;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Connections;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;

namespace P2PMessenger.UI.Tests;

internal sealed class UiHarness : IAsyncDisposable
{
    private readonly List<Window> windows = [];

    private UiHarness(AppServices services, FakePlatform platform)
    {
        Services = services;
        Platform = platform;
        Shell = new MainViewModel(services);
    }

    public AppServices Services { get; }

    public FakePlatform Platform { get; }

    public MainViewModel Shell { get; }

    public Messenger Messenger => Services.Messenger ?? throw new InvalidOperationException("The messenger has not started.");

    public static UiHarness Create() => Create(new InMemoryLocalData());

    public static UiHarness Create(ILocalData localData)
    {
        FakePlatform platform = new();
        MessengerSetup setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Connection = new ConnectionOptions
            {
                ReconnectDelayMin = TimeSpan.FromMilliseconds(50),
                ReconnectDelayMax = TimeSpan.FromMilliseconds(400),
            },
        };
        AppServices services = new(localData, setup, new AvaloniaUiThread(), platform, platform);
        return new UiHarness(services, platform);
    }

    public static async Task<UiHarness> StartAsync(string name)
    {
        UiHarness harness = Create();
        Assert.True((await harness.Services.OpenLocalDataAsync(CancellationToken.None)).IsOpen);
        CreateIdentityResult created = await harness.Services.Identities.CreateAsync(name, CancellationToken.None);
        Assert.True(created.IsCreated);
        await harness.Shell.StartAsync(CancellationToken.None);
        Assert.NotNull(harness.Services.Messenger);
        return harness;
    }

    public static void Flush() => Dispatcher.UIThread.RunJobs();

    public static T Find<T>(Control root, string name)
        where T : Control =>
        root.FindControl<T>(name) ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");

    public static T Descendant<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name} below the root.");

    public static IReadOnlyList<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible).Select(static block => block.Text ?? string.Empty)];

    public Window Show(Control content)
    {
        Window window = new() { Content = content, Width = 800, Height = 900 };
        windows.Add(window);
        window.Show();
        Flush();
        return window;
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 20 seconds.");
            Flush();
            await Task.Delay(20);
        }

        Flush();
    }

    public async Task<Contact> AddContactAsync(KeySet keys, string name)
    {
        Assert.True(DisplayName.TryCreate(name, out DisplayName? displayName));
        AddContactResult result = await Messenger.Contacts.AddAsync(Messenger.Contacts.Check(ContactCode.Create(keys, displayName)), CancellationToken.None);
        Assert.True(result.IsSaved);
        Flush();
        return result.Contact;
    }

    public static async Task<Messenger> StartPeerAsync(string name)
    {
        Assert.True(DisplayName.TryCreate(name, out DisplayName? displayName));
        MessengerSetup setup = MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0 };
        return await Messenger.StartAsync(new OwnIdentity(KeySet.Create(), displayName), setup, CancellationToken.None);
    }

    public static IPEndPoint EndpointOf(Messenger peer) =>
        new(IPAddress.Loopback, peer.Connections.ListeningPort ?? throw new InvalidOperationException("The peer is not listening."));

    public async Task<Messenger> ConnectPeerAsync(string name)
    {
        Messenger peer = await StartPeerAsync(name);
        Assert.True((await peer.Contacts.AddAsync(peer.Contacts.Check(Messenger.Identity.CodeText), CancellationToken.None)).IsSaved);
        Assert.True((await Messenger.Contacts.AddAsync(Messenger.Contacts.Check(peer.Identity.CodeText), CancellationToken.None)).IsSaved);
        int port = Messenger.Connections.ListeningPort ?? throw new InvalidOperationException("The messenger is not listening.");
        Assert.True(peer.Connections.SetAddress(Messenger.Identity.Keys.IdentityKey, new IPEndPoint(IPAddress.Loopback, port)));
        await WaitUntilAsync(() =>
            Messenger.Contacts.TryGet(peer.Identity.Keys.IdentityKey, out Contact? contact)
            && contact.IsAdded
            && Messenger.Connections.IsConnected(peer.Identity.Keys.IdentityKey));
        return peer;
    }

    public Contact ContactOf(Messenger peer)
    {
        Assert.True(Messenger.Contacts.TryGet(peer.Identity.Keys.IdentityKey, out Contact? contact));
        return contact;
    }

    public static async Task StopPeerAsync(Messenger peer)
    {
        await peer.DisposeAsync();
        peer.Identity.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Window window in windows)
        {
            window.Close();
        }

        await Services.DisposeAsync();
    }
}
