using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tcp;

namespace P2PMessenger.Integration.Tests.Connections;

internal sealed class TestPeer : IAsyncDisposable
{
    private readonly List<SessionContact> owned = [];

    private TestPeer(KeySet keys, ConnectionOptions options, LogCapture? log, IFailureSink? failures)
    {
        Keys = keys;
        Manager = new ConnectionManager(keys, Contacts, Observer, options, TimeProvider.System, LoggerFor<ConnectionManager>(log), failures);
        Listener = TcpConnectionListener.Start(Manager, new IPEndPoint(IPAddress.Loopback, 0), LoggerFor<TcpConnectionListener>(log));
    }

    public KeySet Keys { get; }

    public KnownContacts Contacts { get; } = new();

    public RecordingObserver Observer { get; } = new();

    public ConnectionManager Manager { get; }

    public TcpConnectionListener Listener { get; }

    public IPEndPoint Endpoint => Listener.LocalEndpoint;

    public static TestPeer Start(ConnectionOptions? options = null, LogCapture? log = null, IFailureSink? failures = null) =>
        new(KeySet.Create(), options ?? TestOptions.Fast, log, failures);

    public SessionContact Know(KeySet other)
    {
        var contact = TestContacts.Create(Keys, other);
        owned.Add(contact);
        if (!Contacts.TryAdd(contact))
        {
            throw new InvalidOperationException("The test contact is already known.");
        }

        return contact;
    }

    public SessionContact Know(TestPeer other) => Know(other.Keys);

    public TcpDialer Dialer => new(Endpoint);

    public Task<ConnectResult> ConnectAsync(SessionContact contact, TestPeer other, CancellationToken cancellationToken) =>
        Manager.ConnectAsync(contact, other.Dialer, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Listener.DisposeAsync();
        await Manager.DisposeAsync();
        foreach (var contact in owned)
        {
            contact.Dispose();
        }

        Keys.Dispose();
    }

    private static ILogger<T> LoggerFor<T>(LogCapture? log) =>
        log is null ? NullLogger<T>.Instance : new CapturingLogger<T>(log);
}
