using System.Diagnostics;
using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Spikes.Nearby.Noise;

public sealed class KkHandshake : IDisposable
{
    public const int MaxMessageLength = 65535;
    public const int DhLength = 32;
    public const int MessageOverhead = DhLength + CipherState.TagLength;

    private static readonly Token[][] MessagePatterns =
    [
        [Token.E, Token.Es, Token.Ss],
        [Token.E, Token.Ee, Token.Se],
    ];

    private readonly bool _initiator;
    private readonly Key _localStatic;
    private readonly PublicKey _remoteStatic;
    private readonly byte[]? _fixedEphemeral;
    private SymmetricState? _state;
    private Key? _ephemeral;
    private PublicKey? _remoteEphemeral;
    private int _nextMessage;
    private bool _failed;
    private bool _disposed;
    private byte[]? _handshakeHash;
    private NoiseTransport? _transport;

    public KkHandshake(
        NoiseProtocol protocol,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        Key localStatic,
        ReadOnlySpan<byte> remoteStaticPublicKey,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(localStatic);
        if (localStatic.Algorithm != KeyAgreementAlgorithm.X25519)
        {
            throw new ArgumentException("The static key is not an X25519 key.", nameof(localStatic));
        }

        if (remoteStaticPublicKey.Length != DhLength)
        {
            throw new ArgumentException("The remote static key is not 32 bytes long.", nameof(remoteStaticPublicKey));
        }

        if (!fixedEphemeralPrivateKey.IsEmpty && fixedEphemeralPrivateKey.Length != DhLength)
        {
            throw new ArgumentException("The ephemeral key is not 32 bytes long.", nameof(fixedEphemeralPrivateKey));
        }

        _initiator = initiator;
        _localStatic = localStatic;
        _remoteStatic = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteStaticPublicKey, KeyBlobFormat.RawPublicKey);
        _fixedEphemeral = fixedEphemeralPrivateKey.IsEmpty ? null : fixedEphemeralPrivateKey.ToArray();

        byte[] localStaticPublicKey = localStatic.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        _state = new SymmetricState(protocol.Hash, protocol.NameBytes);
        _state.MixHash(prologue);
        _state.MixHash(initiator ? localStaticPublicKey : remoteStaticPublicKey);
        _state.MixHash(initiator ? remoteStaticPublicKey : localStaticPublicKey);
    }

    private enum Token
    {
        E,
        Ee,
        Es,
        Se,
        Ss,
    }

    public bool IsCompleted => _transport is not null;

    public bool HasFailed => _failed;

    public ReadOnlySpan<byte> HandshakeHash => _handshakeHash ?? throw new InvalidOperationException("The handshake is not complete.");

    public NoiseTransport Transport => _transport ?? throw new InvalidOperationException("The handshake is not complete.");

    private bool IsWriteTurn => (_nextMessage % 2 == 0) == _initiator;

    private Key Ephemeral => _ephemeral ?? throw new InvalidOperationException("No ephemeral key yet.");

    private PublicKey RemoteEphemeral => _remoteEphemeral ?? throw new InvalidOperationException("No remote ephemeral key yet.");

    public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> message)
    {
        SymmetricState state = ActiveState();
        if (!IsWriteTurn)
        {
            throw new InvalidOperationException("It is the other party's turn to write.");
        }

        if (payload.Length > MaxMessageLength - MessageOverhead)
        {
            throw new ArgumentException("The payload does not fit into one Noise message.", nameof(payload));
        }

        if (message.Length < payload.Length + MessageOverhead)
        {
            throw new ArgumentException("The message buffer is too small.", nameof(message));
        }

        int offset = 0;
        foreach (Token token in MessagePatterns[_nextMessage])
        {
            if (token == Token.E)
            {
                _ephemeral = CreateEphemeral();
                Span<byte> ephemeralPublicKey = message.Slice(offset, DhLength);
                if (!_ephemeral.PublicKey.TryExport(KeyBlobFormat.RawPublicKey, ephemeralPublicKey, out int written) || written != DhLength)
                {
                    throw new InvalidOperationException("The ephemeral public key could not be written.");
                }

                state.MixHash(ephemeralPublicKey);
                offset += DhLength;
            }
            else if (!TryMixDh(state, token))
            {
                Fail();
                throw new InvalidOperationException("The key agreement gave an all-zero result.");
            }
        }

        offset += state.EncryptAndHash(payload, message[offset..]);
        Advance(state);
        return offset;
    }

    public bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength)
    {
        payloadLength = 0;
        if (_failed)
        {
            return false;
        }

        SymmetricState state = ActiveState();
        if (IsWriteTurn)
        {
            throw new InvalidOperationException("It is this party's turn to write.");
        }

        if (message.Length > MaxMessageLength || message.Length < MessageOverhead)
        {
            return Fail();
        }

        int offset = 0;
        foreach (Token token in MessagePatterns[_nextMessage])
        {
            if (token == Token.E)
            {
                ReadOnlySpan<byte> ephemeralPublicKey = message.Slice(offset, DhLength);
                if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, ephemeralPublicKey, KeyBlobFormat.RawPublicKey, out _remoteEphemeral))
                {
                    return Fail();
                }

                state.MixHash(ephemeralPublicKey);
                offset += DhLength;
            }
            else if (!TryMixDh(state, token))
            {
                return Fail();
            }
        }

        if (!state.TryDecryptAndHash(message[offset..], payload, out payloadLength))
        {
            return Fail();
        }

        Advance(state);
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseHandshakeState();
        _transport?.Dispose();
    }

    private SymmetricState ActiveState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed)
        {
            throw new InvalidOperationException("The handshake has failed.");
        }

        return _state ?? throw new InvalidOperationException("The handshake is already complete.");
    }

    private Key CreateEphemeral()
    {
        _ephemeral?.Dispose();
        return _fixedEphemeral is null
            ? Key.Create(KeyAgreementAlgorithm.X25519)
            : Key.Import(KeyAgreementAlgorithm.X25519, _fixedEphemeral, KeyBlobFormat.RawPrivateKey);
    }

    private bool TryMixDh(SymmetricState state, Token token)
    {
        (Key privateKey, PublicKey publicKey) = token switch
        {
            Token.Ee => (Ephemeral, RemoteEphemeral),
            Token.Es => _initiator ? (Ephemeral, _remoteStatic) : (_localStatic, RemoteEphemeral),
            Token.Se => _initiator ? (_localStatic, RemoteEphemeral) : (Ephemeral, _remoteStatic),
            Token.Ss => (_localStatic, _remoteStatic),
            _ => throw new UnreachableException(),
        };

        SharedSecretCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(privateKey, publicKey, in parameters);
        if (sharedSecret is null)
        {
            return false;
        }

        Span<byte> dhOutput = stackalloc byte[DhLength];
        try
        {
            if (!sharedSecret.TryExport(SharedSecretBlobFormat.RawSharedSecret, dhOutput, out int written) || written != DhLength)
            {
                throw new InvalidOperationException("The X25519 output could not be exported.");
            }

            state.MixKey(dhOutput);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dhOutput);
        }
    }

    private void Advance(SymmetricState state)
    {
        _nextMessage++;
        if (_nextMessage < MessagePatterns.Length)
        {
            return;
        }

        (CipherState initiatorToResponder, CipherState responderToInitiator) = state.Split();
        _handshakeHash = state.HandshakeHash.ToArray();
        _transport = _initiator
            ? new NoiseTransport(initiatorToResponder, responderToInitiator)
            : new NoiseTransport(responderToInitiator, initiatorToResponder);
        ReleaseHandshakeState();
    }

    private bool Fail()
    {
        _failed = true;
        ReleaseHandshakeState();
        return false;
    }

    private void ReleaseHandshakeState()
    {
        _state?.Dispose();
        _state = null;
        _ephemeral?.Dispose();
        _ephemeral = null;
        _remoteEphemeral = null;
    }
}
