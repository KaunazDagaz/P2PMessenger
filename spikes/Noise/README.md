# Spike S04: Noise KK on libsodium

Spike code for step S04 of the development plan. It is never merged into `main`. It compares two ways to run `Noise_KK_25519_ChaChaPoly_BLAKE2b` on libsodium:

- **Noise.NET 1.0.0**, a Noise library for .NET built on libsodium (last release 2018, Noise revision 33);
- **our own** `CipherState`, `SymmetricState` and KK handshake per Noise revision 34, with every primitive from NSec.Cryptography 26.4.0.

It checks what NSec offers for Noise (raw X25519 output, HMAC), runs the Noise test vectors on both, lets the two talk to each other, checks that both refuse changed, replayed, reordered, shortened, oversized and wrongly keyed messages, checks that NSec and Noise.NET load the same libsodium, and measures both.

| Path | Contents |
| --- | --- |
| `P2PMessenger.Spikes.Noise` | Library: our own Noise KK (`Own/`), adapters for both candidates (`Candidates/`), the checks (`Checks/`), the test vectors (`Vectors/`), the benchmark |
| `P2PMessenger.Spikes.Noise.Cli` | The `p2pm-noise-spike` console app |
| `P2PMessenger.Spikes.Noise.Android` | The same self-test and benchmark as an Android app (section 4) |
| `Directory.Packages.props` | Adds the versions of NSec.Cryptography and Noise.NET to the repository's central package versions |

The projects are not part of `P2PMessenger.slnx`, so the architecture tests and CI do not see them. NSec.Cryptography brings libsodium 1.0.22 (the `libsodium` package); Noise.NET asks for libsodium 1.0.16 or later, so NuGet gives both the same package. `run/` is ignored by Git.

## 1. Self-test

Windows, from the repository root:

```
dotnet run --project spikes\Noise\P2PMessenger.Spikes.Noise.Cli -- selftest
```

Linux:

```
dotnet run --project spikes/Noise/P2PMessenger.Spikes.Noise.Cli -- selftest
```

The first build restores the packages and writes a `packages.lock.json` next to each spike project. The self-test prints one line per check and a summary, and writes the same output to `spikes/Noise/run/selftest-<time>.txt`. Expected result: `0 failed`. It runs in a few seconds and needs no network.

| Section | What it checks |
| --- | --- |
| What NSec offers for Noise | The raw X25519 output through `SharedSecret.Export` (RFC 7748 vector), that a shared secret cannot be exported by default, which MAC algorithms NSec has (no HMAC-BLAKE2b), that our HMAC construction equals NSec's HMAC-SHA-256 and HMAC-SHA-512, and NSec's SHA3-256 (FIPS 202 vector; for the onion address in S08) |
| Fixed ephemeral keys | Whether each candidate can be given a fixed ephemeral key, which replaying a test vector needs. Noise.NET has no public way; the spike reaches its internal test hook by reflection and generated code, and says so when the runtime does not allow it |
| Noise test vectors | `Noise_KK_25519_ChaChaPoly_*` with BLAKE2b, SHA256 and SHA512, from cacophony and snow (section 5), on both candidates: every message written and read as in the vector, and the handshake hash |
| Interoperability | Each candidate calls the other, with random keys, for the three hash functions: the same handshake hash, and transport messages of 0 to 65,519 bytes both ways |
| Rejections | For both candidates: message 1 from a key that is not the expected contact, message 1 meant for another key, another prologue, every single-bit change of message 1, message 2 and a transport message, every shortened length and one extra byte, replayed and reordered transport messages, messages over 65,535 bytes |
| libsodium | Whether NSec and Noise.NET get the same library handle, and which libsodium files the process has mapped |

## 2. Benchmark

```
dotnet run --project spikes\Noise\P2PMessenger.Spikes.Noise.Cli -c Release -- bench
```

It times 300 KK handshakes (both parties in one process) and 2,000 transport messages of 1,024 bytes per candidate, after a warm-up, and prints the median and 90th percentile of the handshakes and the mean time per message. `bench <handshakes> <messages>` changes the counts.

## 3. libsodium's Android builds

```
dotnet run --project spikes\Noise\P2PMessenger.Spikes.Noise.Cli -- android-libs
```

After the first build, NuGet keeps the `libsodium` package in its global folder (`%USERPROFILE%\.nuget\packages`, or `NUGET_PACKAGES` when set). The command reads the ELF program headers of every Android `libsodium.so` there and checks that the 64-bit ones are 16 KB-aligned. A folder can be given: `android-libs <packages folder>`.

## 4. Android test app

`P2PMessenger.Spikes.Noise.Android` is a plain .NET for Android app, without Avalonia, that runs the same self-test and benchmark on a phone. It needs Android 8.0 (API 26) or later, the android workload, the Android SDK and a JDK (see the repository README), and a phone with USB debugging.

```
dotnet build spikes\Noise\P2PMessenger.Spikes.Noise.Android -t:Run
```

The top of the log shows the device, its page size and the files in the app's native library folder, where `libsodium.so` should be. The buttons:

- **Self-test:** section 1 on the phone.
- **Benchmark:** section 2 on the phone (a Debug build, so slower than a release build would be).
- **Copy log** copies the log to the clipboard.

`adb logcat -s NoiseSpike` shows the log while it runs. The app saves each run's log in its private `files/run` folder; it is a debug build, so `adb` can fetch it through `run-as`:

```
adb shell "run-as io.github.kaunazdagaz.p2pmessenger.noisespike sh -c 'cd files/run && tar -cf - *.txt' > /data/local/tmp/noisespike.tar"
adb pull /data/local/tmp/noisespike.tar spikes\Noise\run\android.tar
adb shell rm /data/local/tmp/noisespike.tar
mkdir spikes\Noise\run\android -Force
tar -xf spikes\Noise\run\android.tar -C spikes\Noise\run\android
```

## 5. Test vectors

`P2PMessenger.Spikes.Noise/Vectors/noise-kk-vectors.json` holds the six `Noise_KK_25519_ChaChaPoly_*` vectors (BLAKE2b, SHA256 and SHA512, without pre-shared keys) taken unchanged, apart from an added `source` field, from:

| Source | File and commit | Licence |
| --- | --- | --- |
| cacophony (Haskell) | `vectors/cacophony.txt` in `haskell-cryptography/cacophony`, commit `18b7348c54fd61fcd0c220298883de0d09c8364d` | Public domain (Unlicense) |
| snow (Rust) | `tests/vectors/snow.txt` in `mcginty/snow`, commit `d00b360cc61a7fe519ce7539974dca4f36c4654a` | Apache-2.0 or MIT |

The cacophony vectors have six messages and the handshake hash; the snow vectors have four messages and no handshake hash.

## What the spike writes to disk

Only its logs, in `run/` (on the phone, in the app's private `files/run`). The keys are the public test keys of the vectors and random keys made for each check, which stay in memory and are never printed.
