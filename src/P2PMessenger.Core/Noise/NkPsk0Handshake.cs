using System.Diagnostics;
using System.Text;
using NSec.Cryptography;
using P2PMessenger.Core.Crypto;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class NkPsk0Handshake : IDisposable
{
    public const int MaxMessageLength = 65535;
    public const int DhLength = X25519Agreement.OutputLength;
    public const int PreSharedKeyLength = 32;
    public const int MessageOverhead = DhLength + CipherState.TagLength;

    private const string ProtocolNamePrefix = "Noise_NKpsk0_25519_ChaChaPoly_";

    private static readonly Token[][] MessagePatterns =
    [
        [Token.Psk, Token.E, Token.Es],
        [Token.E, Token.Ee],
    ];

    private readonly bool initiator;
    private readonly Key? localStatic;
    private readonly PublicKey? remoteStatic;
    private readonly byte[] preSharedKey;
    private readonly byte[]? fixedEphemeral;
    private SymmetricState? state;
    private Key? ephemeral;
    private PublicKey? remoteEphemeral;
    private int nextMessage;
    private bool failed;
    private bool disposed;
    private byte[]? handshakeHash;
    private TransportCipher? transport;

    private NkPsk0Handshake(
        NoiseHash hash,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> preSharedKey,
        Key? localStatic,
        PublicKey? remoteStatic,
        ReadOnlySpan<byte> responderStaticPublicKey,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey)
    {
        if (preSharedKey.Length != PreSharedKeyLength)
        {
            throw new ArgumentException("The pre-shared key is not 32 bytes long.", nameof(preSharedKey));
        }

        if (!fixedEphemeralPrivateKey.IsEmpty && fixedEphemeralPrivateKey.Length != DhLength)
        {
            throw new ArgumentException("The ephemeral key is not 32 bytes long.", nameof(fixedEphemeralPrivateKey));
        }

        this.initiator = initiator;
        this.localStatic = localStatic;
        this.remoteStatic = remoteStatic;
        this.preSharedKey = preSharedKey.ToArray();
        fixedEphemeral = fixedEphemeralPrivateKey.IsEmpty ? null : fixedEphemeralPrivateKey.ToArray();

        state = new SymmetricState(hash, Encoding.ASCII.GetBytes(ProtocolNamePrefix + hash.Name));
        state.MixHash(prologue);
        state.MixHash(responderStaticPublicKey);
    }

    private enum Token
    {
        Psk,
        E,
        Ee,
        Es,
    }

    public bool IsCompleted => handshakeHash is not null;

    public bool HasFailed => failed;

    public ReadOnlySpan<byte> HandshakeHash => handshakeHash ?? throw new InvalidOperationException("The handshake is not complete.");

    private bool IsWriteTurn => (nextMessage % 2 == 0) == initiator;

    private Key Ephemeral => ephemeral ?? throw new InvalidOperationException("No ephemeral key yet.");

    private PublicKey RemoteEphemeral => remoteEphemeral ?? throw new InvalidOperationException("No remote ephemeral key yet.");

    public static NkPsk0Handshake CreateInitiator(
        NoiseHash hash,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> preSharedKey,
        X25519PublicKey remoteStatic,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(remoteStatic);
        if (!remoteStatic.TryImportNsec(out PublicKey? remote))
        {
            throw new ArgumentException("The remote static key cannot be imported.", nameof(remoteStatic));
        }

        return new NkPsk0Handshake(hash, true, prologue, preSharedKey, null, remote, remoteStatic.Bytes, fixedEphemeralPrivateKey);
    }

    public static NkPsk0Handshake CreateResponder(
        NoiseHash hash,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> preSharedKey,
        X25519KeyPair localStatic,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(localStatic);

        return new NkPsk0Handshake(hash, false, prologue, preSharedKey, localStatic.NsecKey, null, localStatic.PublicKey.Bytes, fixedEphemeralPrivateKey);
    }

    public bool TryWriteMessage(ReadOnlySpan<byte> payload, Span<byte> message, out int written)
    {
        written = 0;
        SymmetricState active = ActiveState();
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
        foreach (Token token in MessagePatterns[nextMessage])
        {
            if (token == Token.Psk)
            {
                active.MixKeyAndHash(preSharedKey);
            }
            else if (token == Token.E)
            {
                Key newEphemeral = CreateEphemeral();
                ephemeral = newEphemeral;
                Span<byte> ephemeralPublicKey = message.Slice(offset, DhLength);
                if (!newEphemeral.PublicKey.TryExport(KeyBlobFormat.RawPublicKey, ephemeralPublicKey, out int exported) || exported != DhLength)
                {
                    throw new InvalidOperationException("The ephemeral public key could not be written.");
                }

                active.MixHash(ephemeralPublicKey);
                active.MixKey(ephemeralPublicKey);
                offset += DhLength;
            }
            else if (!TryMixDh(active, token))
            {
                return Fail();
            }
        }

        offset += active.EncryptAndHash(payload, message[offset..]);
        Advance(active);
        written = offset;
        return true;
    }

    public bool TryReadMessage(ReadOnlySpan<byte> message, Span<byte> payload, out int payloadLength)
    {
        payloadLength = 0;
        if (failed)
        {
            return false;
        }

        SymmetricState active = ActiveState();
        if (IsWriteTurn)
        {
            throw new InvalidOperationException("It is this party's turn to write.");
        }

        if (message.Length > MaxMessageLength || message.Length < MessageOverhead)
        {
            return Fail();
        }

        int offset = 0;
        foreach (Token token in MessagePatterns[nextMessage])
        {
            if (token == Token.Psk)
            {
                active.MixKeyAndHash(preSharedKey);
            }
            else if (token == Token.E)
            {
                ReadOnlySpan<byte> ephemeralPublicKey = message.Slice(offset, DhLength);
                if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, ephemeralPublicKey, KeyBlobFormat.RawPublicKey, out PublicKey? newRemoteEphemeral)
                    || newRemoteEphemeral is null)
                {
                    return Fail();
                }

                remoteEphemeral = newRemoteEphemeral;
                active.MixHash(ephemeralPublicKey);
                active.MixKey(ephemeralPublicKey);
                offset += DhLength;
            }
            else if (!TryMixDh(active, token))
            {
                return Fail();
            }
        }

        if (!active.TryDecryptAndHash(message[offset..], payload, out payloadLength))
        {
            return Fail();
        }

        Advance(active);
        return true;
    }

    public TransportCipher TakeTransport()
    {
        TransportCipher currentTransport = transport ?? throw new InvalidOperationException("The handshake has no transport to hand over.");
        transport = null;
        return currentTransport;
    }

    public void Dispose()
    {
        disposed = true;
        state?.Dispose();
        state = null;
        ephemeral?.Dispose();
        ephemeral = null;
        remoteEphemeral = null;
        transport?.Dispose();
        transport = null;
        CryptographicOperations.ZeroMemory(preSharedKey);
        if (handshakeHash is not null)
        {
            CryptographicOperations.ZeroMemory(handshakeHash);
        }

        if (fixedEphemeral is not null)
        {
            CryptographicOperations.ZeroMemory(fixedEphemeral);
        }
    }

    private SymmetricState ActiveState()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (failed)
        {
            throw new InvalidOperationException("The handshake has failed.");
        }

        return state ?? throw new InvalidOperationException("The handshake is already complete.");
    }

    private Key CreateEphemeral()
    {
        ephemeral?.Dispose();
        ephemeral = null;
        return fixedEphemeral is null
            ? Key.Create(KeyAgreementAlgorithm.X25519)
            : Key.Import(KeyAgreementAlgorithm.X25519, fixedEphemeral, KeyBlobFormat.RawPrivateKey);
    }

    private bool TryMixDh(SymmetricState active, Token token)
    {
        (Key privateKey, PublicKey publicKey) = token switch
        {
            Token.Ee => (Ephemeral, RemoteEphemeral),
            Token.Es => initiator ? (Ephemeral, remoteStatic!) : (localStatic!, RemoteEphemeral),
            _ => throw new UnreachableException(),
        };

        Span<byte> dhOutput = stackalloc byte[DhLength];
        try
        {
            if (!X25519Agreement.TryAgree(privateKey, publicKey, dhOutput))
            {
                return false;
            }

            active.MixKey(dhOutput);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dhOutput);
        }
    }

    private void Advance(SymmetricState active)
    {
        nextMessage++;
        if (nextMessage < MessagePatterns.Length)
        {
            return;
        }

        (CipherState initiatorToResponder, CipherState responderToInitiator) = active.Split();
        handshakeHash = active.HandshakeHash.ToArray();
        transport = initiator
            ? new TransportCipher(initiatorToResponder, responderToInitiator)
            : new TransportCipher(responderToInitiator, initiatorToResponder);
        ReleaseHandshakeState();
    }

    private bool Fail()
    {
        failed = true;
        ReleaseHandshakeState();
        return false;
    }

    private void ReleaseHandshakeState()
    {
        state?.Dispose();
        state = null;
        ephemeral?.Dispose();
        ephemeral = null;
        remoteEphemeral = null;
    }
}
