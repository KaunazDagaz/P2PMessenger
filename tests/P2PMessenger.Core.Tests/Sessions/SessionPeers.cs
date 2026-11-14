using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Core.Tests.Sessions;

internal sealed class SessionPeers : IDisposable
{
    private SessionPeers(KeySet alice, KeySet bob)
    {
        Alice = alice;
        Bob = bob;
        BobForAlice = Contact(alice, bob);
        AliceForBob = Contact(bob, alice);
    }

    public KeySet Alice { get; }

    public KeySet Bob { get; }

    public SessionContact BobForAlice { get; }

    public SessionContact AliceForBob { get; }

    public static SessionPeers Create() => new(TestKeys.Alice(), TestKeys.Bob());

    public static SessionPeers CreateRandom() => new(KeySet.Create(), KeySet.Create());

    public static SessionContact Contact(KeySet own, KeySet other)
    {
        if (!SessionContact.TryCreate(own, other.IdentityKey, other.StaticKey, out var contact))
        {
            throw new InvalidOperationException("The test contact was not accepted.");
        }

        return contact;
    }

    public byte[] FirstFlight()
    {
        using var initiator = SessionInitiator.Start(Alice, BobForAlice);
        return initiator.FirstFlight.ToArray();
    }

    public (SecureSession Alice, SecureSession Bob) Connect()
    {
        using var initiator = SessionInitiator.Start(Alice, BobForAlice);
        if (!SessionResponder.TryAccept(Bob, AliceForBob, initiator.FirstFlight, out var responder, out var response))
        {
            throw new InvalidOperationException("The responder refused the test handshake.");
        }

        if (!initiator.TryFinish(response, out var session))
        {
            responder.Dispose();
            throw new InvalidOperationException("The initiator refused the test response.");
        }

        return (session, responder);
    }

    public (SecureSession Alice, SecureSession Bob) ConnectConfirmed()
    {
        var (alice, bob) = Connect();
        var frame = alice.WriteFrame(KeepAlive.Instance);
        if (!bob.TryReadFrame(frame.AsSpan(Frame.HeaderLength), out _))
        {
            alice.Dispose();
            bob.Dispose();
            throw new InvalidOperationException("The responder refused the first transport message.");
        }

        return (alice, bob);
    }

    public void Dispose()
    {
        BobForAlice.Dispose();
        AliceForBob.Dispose();
        Alice.Dispose();
        Bob.Dispose();
    }
}
