using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Noise;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Core.Sessions;

public sealed class SessionInitiator : IDisposable
{
    public const int FirstFlightLength = ConnectionHint.Length + Frame.HeaderLength + Frame.HandshakeMessageLength;
    public const int ResponseLength = Frame.HeaderLength + Frame.HandshakeMessageLength;

    private readonly byte[] _firstFlight;
    private KkHandshake? _handshake;
    private bool _finished;

    private SessionInitiator(SessionContact contact, KkHandshake handshake, byte[] firstFlight)
    {
        Contact = contact;
        _handshake = handshake;
        _firstFlight = firstFlight;
    }

    public SessionContact Contact { get; }

    public ReadOnlySpan<byte> FirstFlight => _firstFlight;

    public static SessionInitiator Start(KeySet own, SessionContact contact)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(contact);

        Span<byte> nonce = stackalloc byte[ConnectionHint.NonceLength];
        RandomNumberGenerator.Fill(nonce);
        return Start(own, contact, nonce, default);
    }

    public bool TryFinish(ReadOnlySpan<byte> response, [NotNullWhen(true)] out SecureSession? session)
    {
        session = null;
        if (_finished)
        {
            throw new InvalidOperationException("The handshake has already finished.");
        }

        KkHandshake? pending = _handshake;
        ObjectDisposedException.ThrowIf(pending is null, this);
        _handshake = null;
        _finished = true;

        using (pending)
        {
            if (response.Length != ResponseLength
                || !Frame.TryReadHandshakeLength(response[..Frame.HeaderLength], out _)
                || !pending.TryReadMessage(response[Frame.HeaderLength..], Span<byte>.Empty, out int payloadLength)
                || payloadLength != 0
                || !pending.IsCompleted)
            {
                return false;
            }

            session = new SecureSession(Contact, isInitiator: true, pending.TakeTransport(), pending.HandshakeHash.ToArray());
            return true;
        }
    }

    public void Dispose()
    {
        _handshake?.Dispose();
        _handshake = null;
    }

    internal static SessionInitiator Start(KeySet own, SessionContact contact, ReadOnlySpan<byte> hintNonce, ReadOnlySpan<byte> fixedEphemeralPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(contact);

        byte[] firstFlight = new byte[FirstFlightLength];
        Span<byte> hint = firstFlight.AsSpan(0, ConnectionHint.Length);
        ConnectionHint.Write(contact, hintNonce, hint);
        byte[] prologue = SessionPrologue.Build(hint, own.IdentityKey, contact.IdentityKey);

        KkHandshake handshake = new(NoiseHash.Blake2b, initiator: true, prologue, own.StaticKeyPair, contact.StaticKey, fixedEphemeralPrivateKey);
        Span<byte> message = firstFlight.AsSpan(ConnectionHint.Length + Frame.HeaderLength);
        if (!handshake.TryWriteMessage(default, message, out int written) || written != Frame.HandshakeMessageLength)
        {
            handshake.Dispose();
            throw new InvalidOperationException("The first handshake message could not be written.");
        }

        BinaryPrimitives.WriteUInt16BigEndian(firstFlight.AsSpan(ConnectionHint.Length, Frame.HeaderLength), (ushort)written);
        return new SessionInitiator(contact, handshake, firstFlight);
    }
}
