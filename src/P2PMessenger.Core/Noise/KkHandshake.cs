using System.Diagnostics;
using System.Text;
using NSec.Cryptography;
using P2PMessenger.Core.Crypto;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class KkHandshake : IDisposable
{
    public const int MaxMessageLength = 65535;
    public const int DhLength = X25519Agreement.OutputLength;
    public const int MessageOverhead = DhLength + CipherState.TagLength;

    private const string ProtocolNamePrefix = "Noise_KK_25519_ChaChaPoly_";

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
    private TransportCipher? _transport;

    public KkHandshake(
        NoiseHash hash,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        X25519KeyPair localStatic,
        X25519PublicKey remoteStatic,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(localStatic);
        ArgumentNullException.ThrowIfNull(remoteStatic);

        if (!remoteStatic.TryImportNsec(out PublicKey? remote))
        {
            throw new ArgumentException("The remote static key cannot be imported.", nameof(remoteStatic));
        }

        if (!fixedEphemeralPrivateKey.IsEmpty && fixedEphemeralPrivateKey.Length != DhLength)
        {
            throw new ArgumentException("The ephemeral key is not 32 bytes long.", nameof(fixedEphemeralPrivateKey));
        }

        _initiator = initiator;
        _localStatic = localStatic.NsecKey;
        _remoteStatic = remote;
        _fixedEphemeral = fixedEphemeralPrivateKey.IsEmpty ? null : fixedEphemeralPrivateKey.ToArray();

        ReadOnlySpan<byte> localStaticPublicKey = localStatic.PublicKey.Bytes;
        ReadOnlySpan<byte> remoteStaticPublicKey = remoteStatic.Bytes;
        _state = new SymmetricState(hash, Encoding.ASCII.GetBytes(ProtocolNamePrefix + hash.Name));
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

    public bool IsCompleted => _handshakeHash is not null;

    public bool HasFailed => _failed;

    public ReadOnlySpan<byte> HandshakeHash => _handshakeHash ?? throw new InvalidOperationException("The handshake is not complete.");

    private bool IsWriteTurn => (_nextMessage % 2 == 0) == _initiator;

    private Key Ephemeral => _ephemeral ?? throw new InvalidOperationException("No ephemeral key yet.");

    private PublicKey RemoteEphemeral => _remoteEphemeral ?? throw new InvalidOperationException("No remote ephemeral key yet.");

    public bool TryWriteMessage(ReadOnlySpan<byte> payload, Span<byte> message, out int written)
    {
        written = 0;
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
                Key ephemeral = CreateEphemeral();
                _ephemeral = ephemeral;
                Span<byte> ephemeralPublicKey = message.Slice(offset, DhLength);
                if (!ephemeral.PublicKey.TryExport(KeyBlobFormat.RawPublicKey, ephemeralPublicKey, out int exported) || exported != DhLength)
                {
                    throw new InvalidOperationException("The ephemeral public key could not be written.");
                }

                state.MixHash(ephemeralPublicKey);
                offset += DhLength;
            }
            else if (!TryMixDh(state, token))
            {
                return Fail();
            }
        }

        offset += state.EncryptAndHash(payload, message[offset..]);
        Advance(state);
        written = offset;
        return true;
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
                if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, ephemeralPublicKey, KeyBlobFormat.RawPublicKey, out PublicKey? remoteEphemeral)
                    || remoteEphemeral is null)
                {
                    return Fail();
                }

                _remoteEphemeral = remoteEphemeral;
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

    public TransportCipher TakeTransport()
    {
        TransportCipher transport = _transport ?? throw new InvalidOperationException("The handshake has no transport to hand over.");
        _transport = null;
        return transport;
    }

    public void Dispose()
    {
        _disposed = true;
        _state?.Dispose();
        _state = null;
        _ephemeral?.Dispose();
        _ephemeral = null;
        _remoteEphemeral = null;
        _transport?.Dispose();
        _transport = null;
        if (_handshakeHash is not null)
        {
            CryptographicOperations.ZeroMemory(_handshakeHash);
        }

        if (_fixedEphemeral is not null)
        {
            CryptographicOperations.ZeroMemory(_fixedEphemeral);
        }
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
        _ephemeral = null;
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

        Span<byte> dhOutput = stackalloc byte[DhLength];
        try
        {
            if (!X25519Agreement.TryAgree(privateKey, publicKey, dhOutput))
            {
                return false;
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
            ? new TransportCipher(initiatorToResponder, responderToInitiator)
            : new TransportCipher(responderToInitiator, initiatorToResponder);
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
