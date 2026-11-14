using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Core.Tests.Sessions;

public sealed class SessionTamperTests
{
    private static readonly int[] TransportBodyLengths = [272, 1040, 4112, 16400];

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Hint_AnySingleBitFlip_IsNotIdentifiedByAnyContact()
    {
        using var peers = SessionPeers.Create();
        var others = Enumerable.Range(0, 20).Select(_ => KeySet.Create()).ToList();
        var contacts = others.Select(other => SessionPeers.Contact(peers.Bob, other)).Append(peers.AliceForBob).ToList();
        try
        {
            var hint = Convert.FromHexString(SessionVectors.Hint);
            Assert.True(ConnectionHint.TryIdentify(hint, contacts, out var caller));
            Assert.Same(peers.AliceForBob, caller);

            for (var bit = 0; bit < hint.Length * 8; bit++)
            {
                Assert.False(ConnectionHint.TryIdentify(Flip(hint, bit), contacts, out _), $"bit {bit}");
            }
        }
        finally
        {
            contacts.Take(others.Count).ToList().ForEach(contact => contact.Dispose());
            others.ForEach(other => other.Dispose());
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Hint_EveryWrongLength_IsNotIdentified()
    {
        using var peers = SessionPeers.Create();
        var hint = Convert.FromHexString(SessionVectors.Hint);

        for (var length = 0; length < 2 * ConnectionHint.Length; length++)
        {
            if (length != ConnectionHint.Length)
            {
                var changed = new byte[length];
                hint.AsSpan(0, Math.Min(length, hint.Length)).CopyTo(changed);
                Assert.False(ConnectionHint.TryIdentify(changed, [peers.AliceForBob], out _), $"length {length}");
            }
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void FirstFlight_AnySingleBitFlip_IsRefused()
    {
        using var peers = SessionPeers.Create();
        var firstFlight = Convert.FromHexString(SessionVectors.FirstFlight);
        Assert.True(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, firstFlight, out var control, out _));
        control.Dispose();

        for (var bit = 0; bit < firstFlight.Length * 8; bit++)
        {
            Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, Flip(firstFlight, bit), out var session, out var response), $"bit {bit}");
            Assert.Null(session);
            Assert.Null(response);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void FirstFlight_EveryTruncationAndExtension_IsRefused()
    {
        using var peers = SessionPeers.Create();
        var firstFlight = Convert.FromHexString(SessionVectors.FirstFlight);

        for (var length = 0; length < firstFlight.Length; length++)
        {
            Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, firstFlight.AsSpan(0, length), out _, out _), $"length {length}");
        }

        foreach (var extra in new byte[][] { [0], [255], new byte[48] })
        {
            Assert.False(SessionResponder.TryAccept(peers.Bob, peers.AliceForBob, [.. firstFlight, .. extra], out _, out _), $"{extra.Length} bytes appended");
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Response_AnySingleBitFlip_IsRefused()
    {
        using var peers = SessionPeers.Create();
        var response = Convert.FromHexString(SessionVectors.Response);
        using (var control = StartFixed(peers))
        {
            Assert.True(control.TryFinish(response, out var session));
            session.Dispose();
        }

        for (var bit = 0; bit < response.Length * 8; bit++)
        {
            using var initiator = StartFixed(peers);
            Assert.False(initiator.TryFinish(Flip(response, bit), out var session), $"bit {bit}");
            Assert.Null(session);
        }
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public void Response_EveryTruncationAndExtension_IsRefused()
    {
        using var peers = SessionPeers.Create();
        var response = Convert.FromHexString(SessionVectors.Response);

        for (var length = 0; length < response.Length; length++)
        {
            using var initiator = StartFixed(peers);
            Assert.False(initiator.TryFinish(response.AsSpan(0, length), out _), $"length {length}");
        }

        using var extended = StartFixed(peers);
        Assert.False(extended.TryFinish([.. response, 0], out _));
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-11")]
    [Trait("NFR", "NFR-09")]
    public void TransportFrame_AnySingleBitFlipOfTheBody_IsRefusedAndClosesTheSession()
    {
        using var peers = SessionPeers.Create();
        var body = Convert.FromHexString(SessionVectors.KeepAliveFrame).AsSpan(Frame.HeaderLength).ToArray();
        using (var control = AcceptFixed(peers))
        {
            Assert.True(control.TryReadFrame(body, out _));
        }

        for (var bit = 0; bit < body.Length * 8; bit++)
        {
            using var bob = AcceptFixed(peers);
            Assert.False(bob.TryReadFrame(Flip(body, bit), out var message), $"bit {bit}");
            Assert.Null(message);
            Assert.True(bob.IsClosed);
            Assert.False(bob.TryReadFrame(body, out _));
        }
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TransportFrame_BodyOfAnyOtherLength_IsRefusedAndClosesTheSession()
    {
        using var peers = SessionPeers.Create();
        var body = Convert.FromHexString(SessionVectors.KeepAliveFrame).AsSpan(Frame.HeaderLength).ToArray();

        foreach (var length in new[] { 0, 1, Frame.TagLength, body.Length - 1, body.Length + 1, 1040, 4112, 16400, 65535 })
        {
            using var bob = AcceptFixed(peers);
            var changed = new byte[length];
            body.AsSpan(0, Math.Min(length, body.Length)).CopyTo(changed);

            Assert.False(bob.TryReadFrame(changed, out _), $"length {length}");
            Assert.True(bob.IsClosed);
        }
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void FrameHeader_EveryValue_IsAcceptedOnlyForTheSizeClasses()
    {
        var accepted = new List<int>();
        var handshake = new List<int>();
        var header = new byte[Frame.HeaderLength];

        for (var value = 0; value <= ushort.MaxValue; value++)
        {
            header[0] = (byte)(value >> 8);
            header[1] = (byte)value;
            if (Frame.TryReadTransportLength(header, out var length))
            {
                Assert.Equal(value, length);
                accepted.Add(value);
            }

            if (Frame.TryReadHandshakeLength(header, out _))
            {
                handshake.Add(value);
            }
        }

        Assert.Equal(TransportBodyLengths, accepted);
        Assert.Equal(Frame.HandshakeMessageLength, Assert.Single(handshake));
        Assert.False(Frame.TryReadTransportLength([1], out _));
        Assert.False(Frame.TryReadTransportLength([1, 16, 0], out _));
    }

    private static SessionInitiator StartFixed(SessionPeers peers) =>
        SessionInitiator.Start(
            peers.Alice,
            peers.BobForAlice,
            Convert.FromHexString(SessionVectors.HintNonce),
            Convert.FromHexString(SessionVectors.InitiatorEphemeral));

    private static SecureSession AcceptFixed(SessionPeers peers)
    {
        if (!SessionResponder.TryAccept(
            peers.Bob,
            peers.AliceForBob,
            Convert.FromHexString(SessionVectors.FirstFlight),
            Convert.FromHexString(SessionVectors.ResponderEphemeral),
            out var session,
            out _))
        {
            throw new InvalidOperationException("The responder refused the vector's first flight.");
        }

        return session;
    }

    private static byte[] Flip(byte[] bytes, int bit)
    {
        var changed = (byte[])bytes.Clone();
        changed[bit / 8] ^= (byte)(1 << (bit % 8));
        return changed;
    }
}
