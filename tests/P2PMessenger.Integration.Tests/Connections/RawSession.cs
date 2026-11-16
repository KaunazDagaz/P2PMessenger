using System.Net;
using System.Net.Sockets;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Integration.Tests.Connections;

internal sealed class RawSession : IDisposable
{
    private RawSession(TcpClient client, SecureSession session, byte[] firstFlight, byte[] response, byte[] confirmation)
    {
        Client = client;
        Stream = client.GetStream();
        Session = session;
        FirstFlight = firstFlight;
        Response = response;
        Confirmation = confirmation;
    }

    public TcpClient Client { get; }

    public NetworkStream Stream { get; }

    public SecureSession Session { get; }

    public byte[] FirstFlight { get; }

    public byte[] Response { get; }

    public byte[] Confirmation { get; }

    public static async Task<RawSession> OpenAsync(IPEndPoint endpoint, KeySet own, SessionContact responder, CancellationToken cancellationToken)
    {
        var client = await Raw.ConnectAsync(endpoint, cancellationToken);
        try
        {
            var stream = client.GetStream();
            using var initiator = SessionInitiator.Start(own, responder);
            var firstFlight = initiator.FirstFlight.ToArray();
            await stream.WriteAsync(firstFlight, cancellationToken);
            var response = new byte[SessionInitiator.ResponseLength];
            await stream.ReadExactlyAsync(response, cancellationToken);
            Assert.True(initiator.TryFinish(response, out var session));
            var confirmation = session.WriteFrame(KeepAlive.Instance);
            await stream.WriteAsync(confirmation, cancellationToken);
            return new RawSession(client, session, firstFlight, response, confirmation);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async Task<RawSession> AcceptAsync(RawServer server, KeySet own, SessionContact caller, CancellationToken cancellationToken)
    {
        var client = await server.AcceptAsync(cancellationToken);
        try
        {
            var stream = client.GetStream();
            var firstFlight = new byte[SessionResponder.FirstFlightLength];
            await stream.ReadExactlyAsync(firstFlight, cancellationToken);
            Assert.True(SessionResponder.TryAccept(own, caller, firstFlight, out var session, out var response));
            await stream.WriteAsync(response, cancellationToken);
            var header = new byte[Frame.HeaderLength];
            await stream.ReadExactlyAsync(header, cancellationToken);
            Assert.True(Frame.TryReadTransportLength(header, out var length));
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, cancellationToken);
            Assert.True(session.TryReadFrame(body, out var first));
            Assert.IsType<KeepAlive>(first);
            return new RawSession(client, session, firstFlight, response, [.. header, .. body]);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken) =>
        await Stream.WriteAsync(bytes, cancellationToken);

    public async Task<SessionMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[Frame.HeaderLength];
        await Stream.ReadExactlyAsync(header, cancellationToken);
        if (!Frame.TryReadTransportLength(header, out var length))
        {
            return null;
        }

        var body = new byte[length];
        await Stream.ReadExactlyAsync(body, cancellationToken);
        return Session.TryReadFrame(body, out var message) ? message : null;
    }

    public void Dispose()
    {
        Session.Dispose();
        Client.Dispose();
    }
}
