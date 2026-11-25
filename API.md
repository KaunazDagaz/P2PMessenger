# P2PMessenger API

P2PMessenger has no server, so it has no HTTP API, no OpenAPI description, no REST resources and no HTTP status codes. Its interfaces are two:

1. **The wire protocol** (Sections 1 to 8): every format that two apps exchange, over the local network, over Tor or through another channel. This is the interface between two running apps, and the one that an independent implementation would have to follow. The data is binary, not JSON, because every connection is an encrypted Noise session with padded frames.
2. **The application interface** (Section 9): the operations, events and result values through which the screens use the back-end libraries inside one app.

**Status:** this document describes the version on the `main` branch. Group messages and the copy of chats to a PC are not built and are not specified here; they will add their own message types. A change to any format changes this document in the same change, and the tests named below compare the code with the reference vectors byte by byte.

## Where the code is

| Part | Code | Tests |
| --- | --- | --- |
| Contact codes, fingerprints, onion addresses (Section 3) | `src/P2PMessenger.Core/Contacts`, `src/P2PMessenger.Core/Identity` | `tests/P2PMessenger.Core.Tests` |
| Pairwise key, hint, prologue, handshake, frames (Sections 4 and 5) | `src/P2PMessenger.Core/Sessions`, `src/P2PMessenger.Core/Noise` | `tests/P2PMessenger.Core.Tests` |
| Message types (Section 6) | `src/P2PMessenger.Core/Messages` | `tests/P2PMessenger.Core.Tests` |
| Connections, admission, reconnects (Section 4.9) | `src/P2PMessenger.Transport/Connections`, `src/P2PMessenger.Transport/Admission`, `src/P2PMessenger.Transport/Tcp` | `tests/P2PMessenger.Transport.Tests`, `tests/P2PMessenger.Integration.Tests` |
| Discovery on the local network (Section 7) | `src/P2PMessenger.Core/Discovery`, `src/P2PMessenger.Transport/Discovery` | `tests/P2PMessenger.Core.Tests`, `tests/P2PMessenger.Transport.Tests` |
| Onion service (Section 8) | `src/P2PMessenger.Transport/Tor`, `src/P2PMessenger.Core/Identity` | `tests/P2PMessenger.Transport.Tests`, `tests/P2PMessenger.Integration.Tests` |
| Application interface (Section 9) | `src/P2PMessenger.Application` | `tests/P2PMessenger.Application.Tests` |

## Contents

1. [Principles](#1-principles)
2. [Versions](#2-versions)
3. [Contact codes](#3-contact-codes)
4. [Connection setup and caller identification](#4-connection-setup-and-caller-identification)
5. [Frames and padding](#5-frames-and-padding)
6. [Message types](#6-message-types)
7. [Discovery on the local network](#7-discovery-on-the-local-network)
8. [Onion service](#8-onion-service)
9. [Application interface](#9-application-interface)

## 1. Principles

These apply to every format below.

- Every connection is a Noise session, `Noise_KK_25519_ChaChaPoly_BLAKE2b`, with both static public keys known in advance. The prologue carries the protocol identifier and version.
- Connections from keys that are not contacts are refused before anything reaches the user.
- Frames are length-prefixed; a Noise message is at most 65,535 bytes. Plaintexts are padded to fixed size classes, and there is a maximum message size.
- Message types are versioned. Parsers are strict, check lengths before allocating and return `false` instead of throwing. On any parse or authentication error the connection is closed without telling the peer why, and unauthenticated data is never used.
- Every message that a person writes has an ID; a stored set of seen IDs rejects replays across sessions. Acknowledgements and keep-alives carry no ID of their own (Section 6).
- Data sent while looking for contacts contains no names, contact lists or messages.
- Nothing in any format carries a private key.

## 2. Versions

| Version | Change |
| --- | --- |
| Contact code 1 | The binary layout and the text form of Section 3 |
| Session 1 (`P2PM/session/v1`) | The pairwise key, the connection hint, the prologue and the KK handshake of Section 4 |
| Frames 1 | 2-byte length, four size classes (Section 5) |
| Text 1, acknowledgement 1, keep-alive 1 | The message types of Section 6 |
| Connection rules 1 | TCP, either side opens, one session per contact and the choice between two, a keep-alive after 15 s without another frame, closing after 45 s without a frame (Section 4.9) |
| Connection rules 2 | Every session has a route, nearby or Tor; a Tor session replaces a nearby one, and a nearby one replaces a Tor one only if the Tor session counted 10 s or more earlier; over Tor 10 s for the hint and 20 s for the handshake (Section 4.9) |
| Announcement 1 (`P2PM/announcement/v1`) | The announcement key, the 274-byte announcement, multicast, directed broadcast and the sweep, a random listening port (Section 7) |

Connection rules 2 change no byte format.

## 3. Contact codes

Format version 1. A contact code carries what a contact needs in order to add its owner: the identity key, the static key, the onion address (which holds the public key of the onion service), a display name and a signature by the identity key. It carries no private key.

### 3.1 The keys of a person

Each device creates three independent keys when the identity is created, from libsodium's random number generator:

| Key | Algorithm | Used for |
| --- | --- | --- |
| Identity key | Ed25519 | Signs the contact code (3.2). Its public key names the person: the fingerprint (3.6) is derived from it |
| Static key | X25519 | The static key of the Noise KK handshake |
| Onion-service key | Ed25519 | The key of the person's v3 onion service. tor gets it at run time and keeps no copy. The onion address is derived from its public key (3.4) |

The identity key binds the other two: the contact code holds the static public key and the onion address, and is signed by the identity key (3.2). The three private keys never leave the device; the app stores them encrypted and no format in this document carries one.

### 3.2 Binary layout, version 1

Every field has a fixed size except the name. The code is 165 + n bytes long, which is 166 to 229 bytes.

| Offset | Size | Field | Rule |
| --- | --- | --- | --- |
| 0 | 1 | Version | 1 |
| 1 | 32 | Identity key | Ed25519 public key |
| 33 | 32 | Static key | X25519 public key; canonical (bit 255 clear and a value below 2^255 - 19) and not of small order |
| 65 | 35 | Onion address | The onion-service public key (32 bytes), the checksum (2 bytes) and the version byte 3, as in 3.4; the version and the checksum must be right (3.7) |
| 100 | 1 | n | Length of the display name in bytes, 1 to 64 |
| 101 | n | Display name | UTF-8, rules in 3.3 |
| 101 + n | 64 | Signature | Ed25519 (RFC 8032) by the identity key over the 20 ASCII bytes `P2PM/contact-code/v1` followed by bytes 0 to 100 + n of the code |

The signature covers the version, the identity key, the static key, the onion address and the name, and its context separates it from every other use of the identity key. Ed25519 signatures are deterministic, so the same keys and name always give the same code.

### 3.3 Display name

The name is what the owner calls themselves. It is a suggestion, not a fact: the app may let the receiver rename the contact, and a name proves nothing about who the owner is.

- 1 to 64 bytes of well-formed UTF-8 (no overlong forms, no surrogates, nothing above U+10FFFF).
- No leading or trailing white space (Unicode White_Space). A name typed by the user is trimmed before it is used; a name in a code that is not already trimmed makes the code invalid, so that every name has exactly one encoding.
- No control characters (category Cc), no line or paragraph separators (Zl, Zp), no private-use or unpaired surrogate code points (Co, Cs), no U+FFFD, and no format characters (Cf) except U+200C and U+200D, which some scripts and emoji need. This leaves out the bidirectional overrides and isolates, the zero-width space, the soft hyphen and the byte order mark, which can make two names look alike or reorder the text around them.
- No normalisation is applied, so the two spellings of an accented letter are two names. A name made only of U+200C, U+200D or combining marks is accepted and shows nothing. The apps therefore show the fingerprint next to a name where it matters, and never rely on names to tell two contacts apart.

### 3.4 Onion address

A person's onion address is computed from the 32-byte public key P of the onion-service key, as Tor's rend-spec v3 defines it, without tor running:

1. checksum = the first 2 bytes of SHA3-256(`.onion checksum` | P | 0x03), where `|` joins bytes;
2. address = Base32 (RFC 4648, lower case, no padding) of P | checksum | 0x03, which is 56 characters, followed by `.onion`.

SHA3-256 comes from libsodium (1.0.21 and later) through NSec, so it is the same on every platform. The code carries the 35 bytes P | checksum | 0x03, not the text of the address; the receiver checks the version byte and the checksum (3.7) and writes the text from the bytes.

### 3.5 Text form

The text form is `P2PM1-` followed by the RFC 4648 Base32 encoding of the binary code, in upper case and without padding (the characters A to Z and 2 to 7). It is one line of 272 to 373 characters. The prefix names the text form and its version; version 1 of the text form holds a version 1 code.

The same text goes into a file and into a QR code. Its characters are in the QR alphanumeric set, so the QR code stays small: with error correction M it has 57 by 57 modules (version 10) for names of 5 and of 12 bytes, 61 by 61 (version 11) for 32 bytes and 65 by 65 (version 12) for the longest name (measured with the Python library segno 1.6.6).

The app's QR code holds exactly this text, in alphanumeric mode with error correction M and a quiet zone of 4 modules. The app tries the encoder's own mask first and, if ZXing's decoder does not read the result without rotation at 3 and 4 pixels per module, the eight masks in turn, and takes the first that reads; every mask gives a valid QR code, so any reader can read it. A reader accepts a QR code whose text passes the rules below, whatever its version, mask or error correction. The saved QR image is a PNG (8-bit grey, 12 pixels per module, the quiet zone included).

Base32 was chosen because other messengers cannot damage it: it has no characters that mark bold, italic or code in Markdown-like syntax, nothing that looks like a link, and no characters that differ between keyboards; its letters survive a change of case, and it leaves out the digits 0, 1, 8 and 9, which look like letters.

A reader accepts what other messengers and editors do to a pasted code, and nothing else:

1. It removes white space (spaces, tabs, line breaks, the non-breaking space and the others of Unicode White_Space), dashes of any kind (so that a code can be written in groups), the soft hyphen, the zero-width characters U+200B to U+200D and U+2060, and the byte order mark U+FEFF, wherever they are.
2. It requires the five characters `P2PM1`, in any case, at the start. `P2PM` followed by another digit is reported as an unsupported version; anything else is not a contact code.
3. It decodes the rest as Base32, in any case, and requires the unused bits of the last character to be zero, so that each code has exactly one canonical text.
4. It parses the bytes (3.7).

It rejects text before or after the code, such as "Here is my code: ...", and input longer than 4,096 characters before it reads further.

### 3.6 Fingerprint

The fingerprint of a person is 30 decimal digits derived from the identity public key:

1. h = BLAKE2b-256(`P2PM/fingerprint/v1` | identity public key), where the label is 19 ASCII bytes;
2. for each group i from 0 to 5, take bytes 5i to 5i + 4 of h as a big-endian integer, reduce it modulo 100,000 and write it as five digits with leading zeros.

The digits are shown in six groups of five, separated by spaces, for example `48720 76951 38111 09183 33093 40162`. That is about 99.7 bits.

Two people compare both fingerprints: the app shows the fingerprint of each of the two identity keys, ordered by comparing the two 32-byte identity public keys as byte strings with the lower one first, so that both screens show the same two numbers in the same order. A person's own fingerprint is also shown in their profile. A fingerprint is stable: it does not change with the session and does not depend on the name.

### 3.7 Validation

A parser accepts a byte string only if it is exactly a version 1 code, and otherwise returns a refusal without throwing and without a partial result:

- the first byte is 1 (otherwise the refusal is "unsupported version"); the length is 166 to 229 bytes and equals 165 + n; n is 1 to 64;
- the onion address has the version byte 3 and a checksum that matches its key (3.4);
- the name follows 3.3;
- the static key follows 3.2: bit 255 is clear, the value is below 2^255 - 19, and an X25519 key agreement with it does not give the all-zero result that libsodium refuses (the keys of small order);
- the signature is a valid Ed25519 signature by the identity key over the context and the body. libsodium's verification also refuses a signature that is not canonical and an identity key that is not a valid public key.

The other refusals are "not a contact code" (the text form has no `P2PM` prefix or is too long) and "damaged" (everything else). Because the signature covers every byte before it, and the parser accepts nothing but the canonical encoding, every single-bit change of a code, every truncation and every extension is refused (tests: a sweep over all bits of four codes, 1,344 to 1,832 bits each).

The key inside the onion address is checked by the checksum and for its length only. libsodium has no function to test an Ed25519 public key, and a signed address whose key is not a valid point is one that tor refuses ("invalid onion address"), which looks like a contact that is not reachable and harms nobody else. The version and the checksum are checked although the signature covers the address, because a signer could sign an address that tor would refuse, and the app must not store it.

### 3.8 What a code proves

A valid signature proves that whoever made the code held the identity private key, and that this holder chose the other two keys and the name. It does not prove that the holder is the person the receiver meant: that is what the in-person scan and the fingerprint comparison are for, and the app says "key checked by you", never "identity verified".

It also does not prove that the holder of the identity key holds the static key. A person can sign a code that carries someone else's static key, and the receiver then pairs that identity with a key that belongs to another person (an unknown key share). The Noise session closes this gap at no cost in messages: the prologue of the KK handshake carries both identity public keys, the initiator's first. A responder whose own identity key is not the one the caller has saved for its static key then fails to derive the same handshake hash, and the handshake fails (Section 4.4).

### 3.9 Reference vector

The keys are published test keys and must never be used for anything else: the identity key is the secret key of RFC 8032, Section 7.1, test 1; the static key is Alice's private key of RFC 7748, Section 6.1; the onion-service key is the secret key of RFC 8032, Section 7.1, test 2. The name is `Alice`. The code (170 bytes) was computed with an independent implementation (Python 3.14, `cryptography` 50.0.2 and `hashlib`), and the tests of `P2PMessenger.Core.Tests` compare the code with it byte by byte.

| Field | Value (hex) |
| --- | --- |
| Version | `01` |
| Identity key | `d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a` |
| Static key | `8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a` |
| Onion address (35 bytes: the key, the checksum `2663` and the version `03`) | `3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c266303` |
| n | `05` |
| Display name | `416c696365` |
| Signature | `5daee82b16429dd5c2ec82935bc454d39f89ab817aaadf2e41f121e0d2a584b0e27223c177c3cf3fe3dc28625bbc026b129614382e3863927d6c4d66cac9d60a` |

Text form (278 characters):

```
P2PM1-AHLVVGABQKYQVN6VJP7NHSLEA45A5YLS6PNKMIZFV4BBU2HXA5IRVBJA6AEYSMFHKR2IW7O4WQ7POWQNX45A2JRYDL2OXJFJR2VJWTTKHVABPQ7IIOEVVEVXBKTU2G36XSOJQLGPF3CJNDGAZVK7CKXUMYGCMYYDAVAWY2LDMVO252BLCZBJ3VOC5SBJGW6EKTJZ7CNLQF5KVXZOIHYSDYGSUWCLBYTSEPAXPQ6PH7R5YKDCLO6AE2YSSYKDQLRYMOJH23CNM3FMTVQK
```

Fingerprint of this identity key: `48720 76951 38111 09183 33093 40162`. Onion address of the onion-service key: `hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd.onion`.

The onion addresses were checked against tor itself: tor 0.4.9.15 from the Tor Expert Bundle 16.0 (Windows, `DisableNetwork 1`) returned, for `ADD_ONION ED25519-V3` with the expanded keys of the four RFC 8032 seeds of `P2PMessenger.Core.Tests`, exactly the four addresses that the tests expect. The addresses of two published services (the Tor Project's and DuckDuckGo's) are also computed back from their public keys.

## 4. Connection setup and caller identification

The bytes are the same on every route: the local network carries them over TCP, and Tor onion services carry them through Tor (Section 8).

### 4.1 Overview

Every connection is one Noise KK handshake. The side that opens the connection is the initiator; either side of a pair can open it. On the wire:

| Direction | Bytes | Length |
| --- | --- | --- |
| Initiator to responder | the hint (4.3), a frame header holding 48, message 1 (4.5): the first flight | 32 + 2 + 48 = 82 bytes |
| Responder to initiator | a frame header holding 48, message 2 (4.5): the response | 2 + 48 = 50 bytes |
| Both ways, afterwards | transport frames (Section 5) | 274, 1,042, 4,114 or 16,402 bytes each |

Nothing else is sent: there is no version negotiation, no greeting and no error message. On any failure the side that notices it closes the connection without sending another byte and without saying why. `P2PMessenger.Core` builds the bytes and checks them and does no I/O; `P2PMessenger.Transport` reads exactly 82 bytes, or 32 and then 50, as the responder, and exactly 50 as the initiator.

### 4.2 Pairwise key

Each pair of contacts shares a pairwise key. Both derive it from their X25519 static keys; it is never sent and never stored.

1. s = X25519(own static private key, the contact's static public key). libsodium refuses the all-zero result of a key of small order, and then the contact cannot be used (the contact code parser refuses such keys already, Section 3.7).
2. PRK = HKDF-Extract with SHA-256 (RFC 5869), salt = the 16 ASCII bytes `P2PM/pairwise/v1`, input = s.
3. key = HKDF-Expand with SHA-256, PRK, info = the 20 ASCII bytes `P2PM/pairwise-key/v1` | the lower static public key | the higher one (compared as 32-byte strings), length 32.

The key is a key for keyed BLAKE2b with a 16-byte output (NSec's `Blake2b_128`) and stays inside an NSec `Key`. It is the same at both ends of the pair, so it names the pair, not the direction. The same extraction gives a second key for the slots of discovery announcements, with its own label (Section 7.2). A contact whose identity key or static key is the user's own is refused.

### 4.3 Connection hint

32 bytes: a nonce of 16 random bytes, new for every connection, followed by the tag, the 16-byte keyed BLAKE2b of the 23 ASCII bytes `P2PM/connection-hint/v1` and the nonce, keyed with the pairwise key.

The responder reads 32 bytes and checks the tag with the pairwise key of each contact in turn (libsodium compares in constant time); the first contact that matches is the caller. If none matches, it closes the connection without sending anything. A check needs no clock, two hints of one pair share no bytes, and to anyone without the pairwise key a hint is 32 random-looking bytes. A hint gives no freshness: it can be recorded and sent again, and freshness comes from the handshake (4.6).

### 4.4 Prologue

The prologue is 111 bytes: the 15 ASCII bytes `P2PM/session/v1`, the 32-byte hint, the initiator's identity public key and the responder's identity public key. Each side writes the identity keys as it has saved them: the initiator its own key and the one from the responder's contact code, the responder the one from the caller's contact code and its own. Noise hashes the prologue before message 1, so a different label, version, hint or identity key on the two sides makes message 1 fail to decrypt. This binds each static key to the identity key that signed its contact code (Section 3.8), and binds message 1 to its hint.

### 4.5 Handshake

`Noise_KK_25519_ChaChaPoly_BLAKE2b`, Noise revision 34, with our own state machine on NSec. The pre-messages are the initiator's static public key and then the responder's, each as its side has saved it.

- Message 1, from the initiator: `e, es, ss` and an empty payload, which is the 32-byte ephemeral public key and the 16-byte tag of the empty payload: 48 bytes.
- Message 2, from the responder: `e, ee, se` and an empty payload: 48 bytes.

Each message goes in a frame whose 2-byte big-endian header holds 48, and any other value is refused. Version 1 carries nothing in the handshake, so a message of another length is refused. After message 2 both sides split the state as Noise defines: the first cipher carries the initiator's transport messages, the second the responder's. The handshake hash, 64 bytes, is the same on both sides; version 1 does not use it.

### 4.6 When a session counts

KK cannot tell a replayed first flight from a new one, and the responder answers either with message 2. Therefore:

- The initiator's session counts as soon as message 2 decrypts, because message 2 depends on the initiator's new ephemeral key.
- The responder's session counts only when the initiator's first transport message decrypts. Until then the responder sends nothing on it (Core refuses to write), and it replaces no older session with the same contact.
- The initiator sends its first transport message straight after message 2: a keep-alive (6.4) if it has nothing else to send.

### 4.7 Refusals and what an observer sees

The responder closes without sending a byte when the hint matches no contact, when the header of message 1 is not 48, or when message 1 does not decrypt: the caller has another key than the contact's, either side has saved another identity key for the other's static key (4.4), or a byte was changed. The initiator closes when the header of message 2 is not 48 or message 2 does not decrypt. A contact who comes back with new keys is closed on like a stranger, because its hint matches no saved pairwise key; the app learns of the change when the person's new code is imported.

An observer of a connection sees 82 bytes, then 50 bytes back, then frames of four sizes (Section 5), and their timing. The two handshake sizes are the same for every connection, so on a local network they show that two devices run this app; on the internet path they travel inside Tor cells.

### 4.8 Reference vector

Alice and Bob are the key sets of `TestKeys` in `P2PMessenger.Core.Tests`: Alice's identity key is the secret key of RFC 8032, Section 7.1, test 1, her static key Alice's private key of RFC 7748, Section 6.1; Bob's identity key is the secret key of RFC 8032 test 3, his static key Bob's private key of RFC 7748. These are published test keys and must never be used for anything else. Alice calls Bob with the hint nonce `000102030405060708090a0b0c0d0e0f`, and the ephemeral keys are those of the cacophony KK vector (`init_ephemeral` for Alice, `resp_ephemeral` for Bob). The values were computed with an independent implementation (Python 3.13 with `cryptography` 50.0.2 and `hashlib`), which first reproduced the six published `Noise_KK_25519_ChaChaPoly_*` vectors of cacophony and snow; the tests of `P2PMessenger.Core.Tests` compare the code with them byte for byte.

| Value | Hex |
| --- | --- |
| Pairwise key (never sent) | `44f5f9cecd6420a1cbfdc68cff686d64e9e2b18e123b5af2d7bbde8958ce5de6` |
| Hint | `000102030405060708090a0b0c0d0e0f09b6657a0ff74cabfc9023c04a8ada01` |
| Prologue | `5032504d2f73657373696f6e2f7631000102030405060708090a0b0c0d0e0f09b6657a0ff74cabfc9023c04a8ada01d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511afc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025` |
| First flight (82 bytes) | `000102030405060708090a0b0c0d0e0f09b6657a0ff74cabfc9023c04a8ada010030ca35def5ae56cec33dc2036731ab14896bc4c75dbb07a61f879f8e3afa4c79448bf4c955a2a5869189c55d8a8dd2ac55` |
| Response (50 bytes) | `003095ebc60d2b1fa672c1f46a8aa265ef51bfe38e7ccb39ec5be34069f14480884332039b24f78bdc1a6aea494d838b3f49` |
| Handshake hash | `1002ef47027e95e1ca504ad42cf09cf3cd9b6c55c8729b858fccd28a9a875338e5e3af8b3dc9e87cb649021860eb56c53bd1f92e22e8e7597674a1bec39f0998` |

The transport frames of this session are in 6.6.

### 4.9 Connections

These rules change no byte format; both apps follow them so that they keep the same session.

- **Transport.** On the local network the bytes of Sections 4 to 6 travel over one TCP connection per session, opened by the initiator. Over Tor the same bytes travel through the onion service (Section 8).
- **Route.** Every session has a route: nearby (a TCP connection on the local network) or Tor (a stream through tor's SOCKS port to the other person's onion service). Both sides know it the same way: the initiator from how it dialled, the responder from where the connection arrived (its TCP listener or its onion target).
- **Who opens.** Either side of a pair may open a session. On the local network both sides dial a contact whose announcement they recognise (Section 7.5).
- **Port.** An app listens on a TCP port chosen at random from 49152 to 65535 at each start, on all its IPv4 addresses, and gives it to its contacts in its announcements (Section 7). The port is a setting of each app, not part of the session format.
- **One session per contact.** An app keeps at most one session with a contact, and a responder's session can take the place of another only after it counts (4.6). When a second session with the same contact counts on the same route, the app keeps the new one if both were opened by the same side, or if the current one counted 10 seconds or more earlier; otherwise, for two sessions opened by different sides within 10 seconds, it keeps the session opened by the side whose static public key is the lower, compared as 32-byte strings. When the second session counts on the other route (connection rules 2): a new Tor session always replaces a nearby one; a new nearby session replaces a Tor one only if the Tor session counted 10 seconds or more earlier, and otherwise the Tor session stays. The other session is closed without a byte sent. Both apps reach the same choice in whichever order the two sessions counted.
- **Keep-alive and idle.** A side that has sent no other frame for 15 seconds sends a keep-alive (6.4). A side closes a connection on which no frame has arrived for 45 seconds.
- **Refusals.** Besides those of 4.7, a responder closes a new connection without reading from it when it already holds too many connections whose sessions have not counted, overall or from the same address, or when new connections arrive faster than its limits; it closes a connection whose 32-byte hint has not arrived in full within 5 seconds, and one whose session has not counted within 10 seconds; over Tor within 10 and 20 seconds. An initiator sends its whole first flight at once, so the hint limit never affects a real caller. Limits and times are each app's own settings, except that the keep-alive interval must stay below the peer's idle timeout.

## 5. Frames and padding

### 5.1 Frames

Every Noise message travels in a frame: a 2-byte big-endian length L, then L bytes. L is 48 for the two handshake messages (Section 4). For a transport message L is the length of the padded plaintext (5.2) plus the 16-byte tag of ChaCha20-Poly1305, one of 272, 1,040, 4,112 and 16,400. A reader checks the header first and refuses any other value before it reads or allocates anything, then reads exactly L bytes.

Transport messages are encrypted with empty associated data and the Noise nonce counter of each direction, so a frame that is replayed, dropped, reordered, shortened or changed fails to decrypt. The session then closes, as it does on every other error, and the sender's app resends what was not acknowledged in a new session; it also resends a message that is still not acknowledged 30 seconds after it went out on a connection that stays open. Noise allows 65,535 bytes per message; version 1 uses at most 16,400.

### 5.2 Padding

The plaintext of a transport message is:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 2 | Content length C, big-endian |
| 2 | C | Content (Section 6) |
| 2 + C | the rest | Zero bytes up to the size class |

The size classes are 256, 1,024, 4,096 and 16,384 bytes, and a sender uses the smallest class that holds 2 + C bytes. A reader refuses a plaintext whose length is not a class, whose C is below 2 or above the class minus 2, whose padding holds a byte other than zero, or whose class is larger than its content needs; so every message has exactly one plaintext. The largest content is 16,382 bytes.

On the wire this leaves four frame sizes. Keep-alives, acknowledgements and texts of up to 220 bytes all give frames of 274 bytes, so an observer cannot tell them apart; a longer text shows only its class.

## 6. Message types

Text messages, acknowledgements and keep-alives, with message IDs and per-sender sequence numbers.

### 6.1 Content

content = type (1 byte) | version (1 byte) | body.

| Type | Version | Message | Body | Content length |
| --- | --- | --- | --- | --- |
| 1 | 1 | Text | 6.2 | 35 to 16,382 bytes |
| 2 | 1 | Acknowledgement | 6.3 | 18 bytes |
| 3 | 1 | Keep-alive | Empty | 2 bytes |

Any other type or version is refused, and the session closes. All apps of a conversation must run the same protocol version. A later release that adds a type or a version needs a new session version or a way for the apps to tell each other what they understand.

### 6.2 Text, version 1

| Offset in the body | Size | Field | Rule |
| --- | --- | --- | --- |
| 0 | 16 | Message ID | 6.5 |
| 16 | 8 | Sequence | Big-endian, signed, 1 to 2^63 - 1: the sender's number for its messages in this chat, from 1 |
| 24 | 8 | Written at | Big-endian, signed, milliseconds since 1970-01-01T00:00:00Z, 0 to 253,402,300,799,999 (the end of the year 9999) |
| 32 | t | Text | Well-formed UTF-8, 1 to 16,348 bytes |

The receiver shows each sender's messages in the order of their sequence numbers and allows gaps, because a resent message may arrive after later ones. A sender never uses a number twice in a chat: when it deletes the chat or a waiting message, it keeps counting after the deleted messages, so the contact's app, which still has them, shows the next message after them. The written-at time comes from the sender's clock; it is shown and never used for order or security. The text is neither trimmed nor normalised: line breaks and every other well-formed character are kept. 16,348 bytes are about 16,000 Latin or 8,000 Cyrillic letters.

### 6.3 Acknowledgement, version 1

The body is the 16-byte ID of a text message that the sender of the acknowledgement has received and stored. A receiver acknowledges every text message it stores, a duplicate included (6.5): it stores the text, or finds its ID among the seen IDs, and only then queues the acknowledgement on the same connection. The sender's app marks a message "received by the contact's app" only when an acknowledgement of its ID arrives in a session with the contact the message was written to, which only that contact's app can open; an acknowledgement of an unknown ID changes nothing. An acknowledgement is not acknowledged.

### 6.4 Keep-alive, version 1

The body is empty. The initiator's first transport message is a keep-alive when it has nothing else to send, which confirms the responder's session (4.6). A side also sends one whenever it has sent no other frame for 15 seconds, and closes a connection on which no frame has arrived for 45 seconds (4.9).

### 6.5 Message IDs and seen IDs

A message ID is 16 random bytes from the operating system's generator, new for each message and kept when the message is resent. The receiver keeps the IDs it has seen (the interface `ISeenMessageIds` of Core, stored in the table `seen_message`, in the same transaction that stores the message) and shows a message whose ID it has seen only once, while it still acknowledges it, so that the sender can stop resending. Within a session the Noise nonce already refuses a replayed frame (5.1); the seen IDs catch a message that comes again in a later session.

### 6.6 Reference vector

The transport frames of the session of 4.8 (Alice's keep-alive and text, then Bob's acknowledgement). The text has the message ID `00112233445566778899aabbccddeeff`, the sequence number 1, the time 1794571200000 (2026-11-13T12:00:00Z) and the text `Hello, Bob`.

| Message | Content (hex) | Frame (hex, header included) |
| --- | --- | --- |
| Keep-alive from Alice | `0301` | `01103d5afa9cd02d304f14fe3cdc80e9521253142d58e65ac420a8492b3c90227a6309305b8cd41dfa34e0ad2852635dc4069f6b2895183eccac483b0fae0a1d9f424ca1faa9aa9ace7106a4df8c5774f0ff07554834ab2a37ef0f80675d191213055bb9e0f0ac53c7809ee3a50f7e252753be85001fe0aeebf4f3e36d11eac527fe4e4cc659bc28bcfda3fbe691fd2a2fc6d649f8b5b3bf6286b1928e5ebe4468946d3182d711fedb0e318c3c482e334fd80fa2f8bd4e0dbdefbf63c3a850eb17b3089876ae25b8502e3cf7538e859d2299cfd1e3a2134fa4804f532c333a7708485f3ea4ae630e21f83e1c85b135c24d29ada7d038417eee94cfac938d8b300fb2be078043574744d87eb1fca78380efb1` |
| Text from Alice | `010100112233445566778899aabbccddeeff0000000000000001000001a1d4c7660048656c6c6f2c20426f62` | `01109447e09b8253ce1a69a26d98edbf259e4b614dfdbe914d2252483ab76fea04954f7eac972880b51865afd423ca25fdefe340e7c749a38111acf004ea85d0524af96a9806abdbb0efe387974af05175254e99d7bcd449f84f872fa07372ce801e8cb77241cc32b9892f93c5584311bb53018a3ad0a9fad440fbd6efb74587f28e1773736656e7ddeba3b2e00b463afd507c89d1391c174758e0d9f4f11eafb7f035b03254e29cbf852766d498e8a7b7d19948ee40c2e0a9138e4f823ee330f502f1c880b71b62eca1a00b68586a8565942ae32d1681ab2f45e66d1449bb05c07ea483f561774fdbe34366a166d62a945ce389f29debcb0de145e0597e4534f11056191f354366f707fea8564cb43a8385` |
| Acknowledgement from Bob | `020100112233445566778899aabbccddeeff` | `0110061c5f95907020de9c791bd5a5602c5722d2991645eb119f12fc2b0f6d3ccec14a3db5fc65c589b3f98317735dbd5ea9e0de16a170e03057f3bceb125c274075d29c46f77eef82b82ecd3af4f18f30450158e10b3d5baf0e15641fcb6fb96f639fa340e0234c6a193dc6d33a1890a41a30d545a0d01578dee861e020177865a1209de756be6a2378e59bb3ad001bfcac2bd31b0138f0a4fe7b7c6265768bec90e6ae3db50c930a2c7bcb78d7517789e74eed15e2f2e2f7e7d4052a9a7abd66b5eafe9331a12e0ce132d29c83c7ede2cf70de354628293de453ad2ccc44f74618fd0e1160733a3dcecf972b7e16d7262b28b0b9a4a6f569c0117d61f302e297db8fe7876079592a7ec90d9d11cb0a02a5` |

A text of 350 bytes (150 times `é` and 50 times `x`, ID `ffeeddccbbaa99887766554433221100`, sequence 42, time 0) has a content of 384 bytes and goes into the 1,024-byte class; its content is in `MessageCodecTests`.

## 7. Discovery on the local network

An app finds its contacts on the same Wi-Fi network or phone's hotspot by private announcements, and then connects to them over TCP as in Section 4. Nothing here is sent over Tor.

### 7.1 Overview

| What | Where | When |
| --- | --- | --- |
| Announcement (7.3) | UDP to 239.255.83.5 port 45821 (multicast, TTL 1) and to the interface's directed broadcast address port 45821, from a socket bound to each local interface (7.4) | Every 3 seconds (±20 %), and at once when a contact is added or the listening port changes |
| The same announcement by unicast | UDP to every other address of each local subnet of at most 510 addresses, port 45821 | Only when no contact has been heard for 30 seconds; at most once a minute, paced (7.6) |
| Connection | TCP to the address the announcement came from and the port in it (7.5) | When a contact is recognised and not connected |

Nothing answers an announcement: there is no reply, query or acknowledgement on UDP.

### 7.2 Announcement key

Each pair of contacts shares an announcement key, derived like the pairwise key of 4.2 and from the same extraction:

1. s = X25519(own static private key, the contact's static public key); PRK = HKDF-Extract with SHA-256, salt `P2PM/pairwise/v1`, input s (as in 4.2).
2. key = HKDF-Expand with SHA-256, PRK, info = the 24 ASCII bytes `P2PM/announcement-key/v1` | the lower static public key | the higher one, length 32.

It is a key for keyed BLAKE2b with a 16-byte output, kept in an NSec `Key`, never sent and never stored. The pairwise key of 4.2 is used only for the hint and this key only for announcements.

### 7.3 Announcement, version 1

274 bytes:

| Bytes | Field |
| --- | --- |
| 0 to 15 | Nonce: 16 random bytes, new for every announcement |
| 16 and 17 | The sender's TCP listening port, big-endian, 1 to 65535 |
| 18 to 273 | 32 slots of 8 bytes |

A recipient's slot is the first 8 bytes of the 16-byte keyed BLAKE2b, under the announcement key of the sender and that recipient, of: the 20 ASCII bytes `P2PM/announcement/v1`, one byte for the side that sent it (1 if the sender's static public key is the lower of the two, compared as 32-byte strings, 2 if it is the higher), the nonce and the port (bytes 0 to 17). The recipients take random slot positions, and the slots of nobody are random bytes. A sender with more than 32 contacts shuffles them and sends several announcements, each with at most 32 recipients, a nonce of its own and the same length. A sender without contacts sends nothing.

To recognise an announcement, a receiver that knows a contact computes the slot that contact would have written for it (the side byte from the contact's side) and looks for it among the 32 slots, comparing in constant time. A match gives the contact and the port. An announcement whose length is not 274 or whose port is 0 is refused.

The side byte keeps a device from taking its own announcement for its contact's: the key is the same at both ends, and every multicast and broadcast datagram comes back to the device that sent it. Without the byte the sender would recognise its own announcement and dial itself.

What an announcement shows: to someone without the announcement key, 274 random-looking bytes and a port. No name, key, contact code, contact list or message is in it; a device with 1 contact sends the same size as one with 32; two announcements share no nonce and no slot value. Anyone on the network sees that a device sends announcements, and can read the port in them.

### 7.4 Sending and receiving

- **Interfaces.** A device announces on each of its local IPv4 interfaces: not loopback, not link-local (169.254/16), with a prefix from /8 to /30, and not mobile data or a VPN (by interface type on the desktops, and on every platform by the name prefixes `rmnet`, `ccmni`, `v4-`, `clat`, `pdp`, `ppp`, `tun`, `wwan` and `wg`). It reads them again every 5 seconds.
- **Sending.** Each announcement goes from a UDP socket bound to the interface's address to 239.255.83.5 (if the interface supports multicast; TTL 1, the interface set as the multicast interface) and to the interface's directed broadcast address, both on port 45821. The two datagrams are identical.
- **Receiving.** One UDP socket bound to port 45821 on all addresses, with address reuse, so that several apps on one computer share it, and joined to 239.255.83.5 on each local interface that supports multicast. A device ignores a datagram whose nonce it sent or already screened in the last 30 seconds, and one whose source address cannot be dialled. It screens at most 100 datagrams a second overall and 10 a second from one source address (bursts of 200 and 20), and ignores the rest. These limits are each app's own settings.

### 7.5 Dialling and reachability

- A device that recognises a contact's announcement keeps the source address of the datagram, the port in the announcement and the time. While the contact is not connected and was heard in the last 30 seconds, it dials that address and port with the reconnect delays of 4.9 (1 to 10 seconds, doubling); a contact not heard for 30 seconds is not dialled until its next announcement. Both sides of a pair do this, and 4.9 keeps one session.
- A contact is reachable nearby only while a session with it counts (4.6). An announcement alone never makes a contact reachable, because it can be recorded and sent again (7.7).
- The listening port is chosen at random from 49152 to 65535 when the app starts and changes with every start; the announcements carry the current one.

### 7.6 Sweep

When the device has contacts and has recognised no announcement for 30 seconds, it also sends each of its current announcements by unicast to every other address of each local subnet of at most 510 addresses (a /23 or smaller), port 45821, from a second socket bound to the interface, in batches of 8 datagrams with 40 ms between batches, and again at most once a minute while it still hears nobody. It covers networks that drop multicast and broadcast between devices but pass unicast. A /24 takes about 1.3 seconds of sending, longer where unanswered addresses wait for address resolution (measured: 3.9 seconds on a Linux bridge).

### 7.7 Replays and other limits

A recorded announcement is recognised again wherever it is sent; it can make a device dial an address the replayer chose, which shows the replayer that the device knows the sender. The session check of Section 4 refuses the handshake, so nothing else follows. There is no time in an announcement. Announcements reach only the local subnet: a contact on another subnet or network is found over Tor (Section 8).

### 7.8 Reference vector

Alice and Bob are the key sets of 4.8 (published test keys; never use them for anything else). The values were computed with an independent implementation (Python 3 with `cryptography` 50 and `hashlib`, outside the repository), which first reproduced the hint of 4.8 from the same extraction; `AnnouncementTests` in `P2PMessenger.Core.Tests` compares the code with them byte for byte. Alice's static public key is the lower of the two, so Alice's announcements carry the side byte 1.

| Value | Hex |
| --- | --- |
| Announcement key (never sent) | `ea7a5e94476ab987c35f907606ae5b72a12105cf2ce8acfb5de9d3ad6af228e7` |
| Nonce, port 50000 | `000102030405060708090a0b0c0d0e0f`, `c350` |
| Alice's slot for Bob (side byte 1) | `bdb1876cafa386dc` |
| Bob's slot for Alice (side byte 2) | `1d00175325ba2382` |

With Bob's slot at position 5 and every other slot all zeros, Alice's announcement is `000102030405060708090a0b0c0d0e0fc350`, 40 zero bytes, `bdb1876cafa386dc` and 216 zero bytes (274 bytes).

## 8. Onion service

The onion service changes no byte of Sections 4 to 6: a connection through Tor carries the same hint, handshake, frames and messages as one on the local network.

- **Address.** The person's onion address is the one in their contact code (3.4), derived from the public key of the onion-service key (3.1).
- **Virtual port.** 45821. A contact connects to `<onion address>.onion:45821` through tor's SOCKS port.
- **Dialling.** The app speaks SOCKS5 (RFC 1928) to its own tor's SOCKS port on a loopback address, which tor opens with `ExtendedErrors`. It offers only username and password authentication (RFC 1929), with a random username of 32 hexadecimal digits for every attempt and the password `p2pm`, so that tor builds a new circuit for each attempt (`IsolateSOCKSAuth`), and asks for `CONNECT` to the domain name `<onion address>.onion`, port 45821. A reply other than success (RFC 1928 codes 1 to 8 and tor's extended codes `0xF0` to `0xF7`, among them `0xF0` for a descriptor that tor cannot find) ends the attempt; on success the reply's bound address and port are ignored, and the stream then carries Sections 4 to 6 as on the local network, the app being the initiator. An attempt has 30 seconds for the SOCKS reply; after a failure the app tries once more at once on a new circuit, then waits 5 seconds, doubling up to 60, before it tries again. These are each app's own settings, not part of the format.
- **Key.** The app gives tor the onion-service key with `ADD_ONION ED25519-V3:<key>` each time tor starts, where `<key>` is the Base64 of the 64-byte Ed25519 expanded secret key: SHA-512 of the 32-byte seed, with the first byte's three low bits cleared, the 32nd byte's top bit cleared and its second-highest bit set. tor keeps no copy on disk. A `ServiceID` from tor that is not the address of the key set fails the start.
- **Target.** Linux and Android: a Unix socket in the app's tor folder (`unix:<path>`; the path is absolute, at most 100 bytes, letters, digits and `/._-` only). Windows, or when the socket cannot be used: a TCP port on 127.0.0.1 chosen by the system. The app takes every connection from the target like an inbound local connection without an address (4.9).

Reference vector: the expanded keys of the four RFC 8032 seeds of `OnionServiceKeyTests` (computed with Python's `hashlib`), for which tor 0.4.9.11 returned the four addresses of `OnionAddressTests`. Seed 2 (`4ccd089b…a6fb`, the onion seed of the vector in 3.9) gives `aL2e11iC1SgVqXWFyvR5Cn9sazt/ghxeJZoksC5QLlFFZoSCkdrK8iXMY96zSNoxjiwuF7ALgWD5zmv6BHKRHQ==` and the address `hvabpq7iioevvevxbktu2g36xsojqlgpf3cjndgazvk7ckxumygcmyyd.onion`.

## 9. Application interface

The screens in `P2PMessenger.UI` reach the back-end only through `P2PMessenger.Application`, inside one process; no network separates them. The entry point is `Messenger`, which owns the identity, the contacts, the chats and the connections of one running app. Expected failures (an empty text, a refused contact code, a locked or damaged database) are returned as result values with the enumerations below, not thrown. An unexpected failure of background work (a connection, discovery, delivery) is not thrown to the screens either: it is reported to one `GlobalFailureHandler`, which logs the type of the exception once and raises `FailureReported`. Changes are announced by events that can be raised on any thread; the screens move them to the UI thread.

### 9.1 Messenger

| Member | Purpose |
| --- | --- |
| `static Messenger.StartAsync(OwnIdentity, MessengerSetup, CancellationToken)` | Starts the listener, the local-network discovery, the Tor route and the outbox for an identity |
| `Identity`, `Contacts`, `Chats`, `Connections`, `InternetRoute` | The services of 9.2 |
| `SendAsync(Ed25519PublicKey contactKey, string? text, CancellationToken)` | Records a text for a contact and hands it to the outbox; returns `SendResult(Outcome, Message)` |
| `DeleteChatAsync(contactKey, CancellationToken)` | Deletes the chat with a contact on this device and cancels its waiting messages; returns the number of messages deleted |
| `DeleteWaitingAsync(contactKey, MessageId, CancellationToken)` | Deletes one message that is still waiting; returns whether it was waiting |
| `DisposeAsync()` | Stops the connections, discovery and tor |

### 9.2 Services

| Service | Operations | Event |
| --- | --- | --- |
| `IdentityService` | `LoadAsync`; `CreateAsync(name, ct)` returns `CreateIdentityResult` | None |
| `ContactBook` | `All`; `TryGet(identityKey, out Contact)`; `Check(text)` and `Check(ContactCode)` return `CodeCheck`; `AddAsync(check[, CodeExchange], ct)` returns `AddContactResult`; `SetKeyComparisonAsync(identityKey, KeyComparison, ct)`; `MarkScannedInPersonAsync(check, ct)` | `Changed` (`ContactChangedEventArgs`) |
| `ChatLog` | `MessagesWith`, `WaitingFor`, `ContactsWithWaitingMessages`, `UnreadCount`, `MarkRead`; `ReceiveAsync`, `MarkReceivedAsync`, `DeleteChatAsync`, `DeleteWaitingAsync` | `Changed` (`ChatChangedEventArgs`) |
| `ConnectionService` | `ListeningPort`, `LastListenResult`, `DiscoveryState`, `DiscoveryPort`, `Network`, `UsesTor`; `IsConnected`, `IsHeard`, `ReachabilityOf`, `DialStatusOf`, `TorStatusOf` per contact | `Changed` (`ConnectionChangedEventArgs`) |
| `InternetRoute` | `Status`, `IsInBuild`, `IsOn`, `TargetKind`; `SetEnabledAsync(on, ct)` turns Tor on or off | `Changed` |
| `GlobalFailureHandler` | `Install()` and `Uninstall()` hook the unobserved task and the process-ending exception; `Handle(exception, FailureSource)` returns false for out-of-memory and otherwise logs the exception's type once (an exception reported again, also inside an `AggregateException`, is ignored); `Report(source, exception)` is the same call for the components; `Count`. It is given to the messenger as `MessengerSetup.Failures` | `FailureReported` (`FailureReport`: the `FailureSource` and the exception's type name, never its message) |

### 9.3 Result values

| Type | Values |
| --- | --- |
| `SendOutcome` | `Queued` (stored and waiting for the contact's app to confirm it); `Empty` (no text or only white space); `TooLong` (more than 16,348 bytes of UTF-8); `NotSendable` (the text format refuses it); `NotAContact`; `WaitingForFirstConnection` (the contact is not yet added because no connection has succeeded) |
| `CodeCheckOutcome` | `New`; `NewConnectionKeys` (a known person with new connection keys); `LooksLikeContact` (a new person with the name of a contact); `AlreadyContact`; `OwnCode`; `KeysInUse` (the keys belong to this device or to another contact); `NotACode`; `UnsupportedVersion`; `Damaged` |
| `AddContactOutcome` | `Added`; `KeysReplaced`; `NotAddable` (the check does not allow adding); `Stale` (the contact list changed since the check) |
| `CreateIdentityError` | `None`; `InvalidName`; `AlreadyCreated` |
| `DeliveryState` | `None` (a received message), `Waiting` and `Received` (a sent message: waiting for, or confirmed by, the contact's app) |
| `Reachability` | `NotReachable`; `Nearby`; `Tor` |
| `FailureSource` | Where a failure was caught: `UiThread`, `BackgroundTask` (a failed task that nobody observed), `AppDomain` (an exception that ends the process), `Connections`, `Discovery`, `Delivery` |
| `InternetRouteState` | `NotInBuild`; `Off`; `Starting`; `Connected`; `Unavailable`, with an `InternetRouteProblem`: `None`, `NotInBuild`, `NoNetwork`, `ClockWrong`, `CannotReachTor`, `TorFailed`, `TorStopped` |
| `LocalDataStatus` | `Opened`; `NotCreated`; `PassphraseNeeded`; `WrongPassphrase`; `PassphraseRequired`; `PassphraseNotAccepted`; `Failed`, with a `LocalDataProblem`: `None`, `KeyStoreUnavailable`, `KeyStoreRefused`, `KeyNotFound`, `NewerVersion`, `Damaged` |

Stored data that fails any check is reported as `Damaged`; the app never replaces data it cannot read. The tables, columns and constraints behind these operations are in [`src/P2PMessenger.Storage/README.md`](src/P2PMessenger.Storage/README.md).

### 9.4 What each platform implements

The back-end libraries call no platform API themselves: whatever differs between Windows, Linux and Android sits behind an interface, and the tests supply fakes for all of them.

| Interface | Purpose | Implemented by |
| --- | --- | --- |
| `IKeyProtector` (Application) | Wraps the database's data key with the platform key store | `DpapiKeyProtector` and `SecretServiceKeyProtector` in `P2PMessenger.Desktop`; `AndroidKeystoreKeyProtector` in `P2PMessenger.Android` |
| `ISettingsStore` (Application) | The few on/off settings of the app | `FileSettingsStore` (Desktop), `PreferenceFlags` (Android), `InMemorySettingsStore` |
| `IBackgroundService`, `IBatteryExemption`, `IMessageNotifier` (Application) | The Android background service, the battery exemption and message notifications | `P2PMessenger.Android` |
| `ILocalInterfaceSource` (Transport) | The device's local network interfaces | `SystemInterfaceSource` (Transport), `AndroidInterfaceSource` (Android) |
| `ILocalData` (Application) | Opens, creates, unlocks and deletes the local database; the passphrase operations | `DatabaseLocalData` (SQLite), `InMemoryLocalData` |
| `IContactStore`, `IChatStore`, `IIdentityStore` (Application) | Persistence of the contacts, the chats and the identity | The database-backed stores in `Application/LocalData`, and the `InMemory` ones |
| `IWakeUpScheduler` (Application) | The timer that wakes the outbox | `TimerWakeUpScheduler` |
| `IFailureSink` (Core) | Receives the unexpected failures that the connections, discovery and delivery catch in their background work; without one they log the failure themselves | `GlobalFailureHandler` (Application), passed in through `MessengerSetup.Failures` |
| `IDialer`, `IDiscoveryLink`, `IKnownContacts` (Transport) | Opening connections, the UDP socket of discovery and the list of contacts that may call | `TcpDialer` and `OnionDialer`, `UdpDiscoveryLink`, `KnownContacts` (Transport) |
| `IConnectionObserver`, `IDiscoveryObserver` (Transport) | Reports of sessions, refusals and heard announcements to the layer above | `ConnectionService` in Application |
