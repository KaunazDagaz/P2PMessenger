# Spike S03: Tor from .NET

Spike code for step S03 of the development plan. It is never merged into `main`. It tries out C tor driven from .NET: tor started as a child process, a control-port client (cookie authentication, bootstrap status, `ADD_ONION` with a key made by the app), a SOCKS5 client, and two onion services that reach each other. It also measures how long tor takes to bootstrap and how long the first connection to a newly created onion service takes.

| Path | Contents |
| --- | --- |
| `get-tor.ps1` | Downloads the Tor Expert Bundle 16.0 (tor 0.4.9.15), checks its OpenPGP signature against the Tor Browser Developers signing key `EF6E286DDA85EA2A4BA7DE684E2C6E8793298290`, and unpacks it into `tor-bundles/` |
| `P2PMessenger.Spikes.Tor` | Library: `TorProcess`, `TorControlClient`, `Socks5Client`, `OnionServiceKey` |
| `P2PMessenger.Spikes.Tor.Cli` | The `p2pm-tor-spike` console app |
| `P2PMessenger.Spikes.Tor.Android` | The same commands as an Android app (section 6) |

The projects are not part of `P2PMessenger.slnx`, so the architecture tests and CI do not see them. The library and the console app use only the .NET base library. `tor-bundles/` and `run/` are ignored by Git.

## 1. Get tor

The script needs `gpg`. Git for Windows includes it, and the script finds it there. On Linux, install the `gnupg` package.

Windows, from the repository root (Windows PowerShell 5.1 or PowerShell 7):

```
powershell -ExecutionPolicy Bypass -File spikes\Tor\get-tor.ps1
```

Linux (PowerShell 7):

```
pwsh spikes/Tor/get-tor.ps1
```

The script prints the SHA-256 of each archive it checked and the path of the tor executable. Other parameters:

- `-Platform windows-x86_64,linux-x86_64,linux-aarch64,android-aarch64,android-armv7,android-x86_64` downloads other bundles; the Android app needs `android-aarch64` and `android-x86_64`.
- `-Force` downloads and unpacks again.
- `-BaseUri <url>` uses a mirror of `https://dist.torproject.org/torbrowser`; the signature is still checked against the same key.
- `-KeyFile <file>` uses a signing key downloaded by hand when none of the key sources can be reached; its fingerprint is still checked.

## 2. Self-test (no Tor network needed)

```
dotnet run --project spikes/Tor/P2PMessenger.Spikes.Tor.Cli -- selftest
```

It checks the event parsers, the SOCKS5 client against a fake proxy, and then two real tor processes: cookie authentication, `ADD_ONION` with an app key, the same address after `DEL_ONION` and in a second tor with an empty data directory, no onion key left in tor's data directory, and tor's `0xF6` reply for a malformed onion address. Expected result: `12 passed, 0 failed.`

## 3. Two onion services on one machine

```
dotnet run --project spikes/Tor/P2PMessenger.Spikes.Tor.Cli -- pair
```

It starts two tor instances, `alice` and `bob`, waits until both have bootstrapped, publishes a new onion service on each (an echo server behind it), waits for the first descriptor upload, and then connects in both directions: the first connection, round trips on it, and reconnects over new circuits. Each connection goes through the Tor network, even though both ends are on the same machine.

The first run starts cold (tor has no cached directory data). Later runs with the same state folder start warm. For another cold run, give a new state folder: `--state spikes/Tor/run/cold-2`.

## 4. Two machines

On machine A:

```
dotnet run --project spikes/Tor/P2PMessenger.Spikes.Tor.Cli -- serve
```

It prints the onion address, then a line every minute while tor runs, and keeps the service up until Ctrl+C. If tor stops on its own, it says so and ends. The key stays in the state folder, so the address is the same on the next run; `--new-key` makes a new one. On machine B:

```
dotnet run --project spikes/Tor/P2PMessenger.Spikes.Tor.Cli -- dial <address>
```

## 5. Metrics

Every command prints a summary table and appends its measurements to `run/results.csv` (time in milliseconds).

| Metric | What it measures |
| --- | --- |
| `bootstrap_cold`, `bootstrap_warm` | From starting the tor process to bootstrap 100%, without and with cached directory data |
| `add_onion` | The `ADD_ONION` command |
| `descriptor_first_upload` | From `ADD_ONION` to the first `HS_DESC UPLOADED` event |
| `first_connect_total` | First connection to a service the client has never contacted: from the first attempt to the connected stream, including failed attempts (their reasons are printed) |
| `first_connect_last_attempt` | The successful attempt alone |
| `round_trip` | 32 bytes to the echo server and back over the open connection |
| `reconnect_new_circuit` | A new connection with a new SOCKS isolation token, which forces tor to build a new circuit; the descriptor is already cached |

## 6. Android test app

`P2PMessenger.Spikes.Tor.Android` is a plain .NET for Android app, without Avalonia, that runs the same commands on a phone. It ships tor from the Tor Expert Bundle (`tor/libTor.so` for arm64 and x86_64) as a native library. Android extracts it at install into the app's native library folder, and the app starts it from there as a separate process, as on the desktop. A foreground service with a notification keeps the app running while a command runs. The project compiles the console app's command files (all but `Program.cs`), so the phone runs the same code as the desktop. It needs Android 8.0 (API 26) or later, because the bundle's tor is built for it.

It needs the android workload, the Android SDK and a JDK (see the repository README), a phone with USB debugging, and the Android bundles:

```
powershell -ExecutionPolicy Bypass -File spikes\Tor\get-tor.ps1 -Platform android-aarch64,android-x86_64
```

Build, install and start it on the connected phone:

```
dotnet build spikes\Tor\P2PMessenger.Spikes.Tor.Android -t:Run
```

The top of the log shows the device, the files in the app's native library folder, and whether tor is among them. The buttons:

- **Self-test:** the checks of section 2, with tor on the phone.
- **Pair:** section 3 on the phone: two tor instances on the phone connect to each other's onion services.
- **Serve:** section 4 with the phone as machine A. To read its address on the PC: `adb logcat -d -s TorSpike | Select-String "Onion address"`. With the screen off, gaps between the minute lines show that Android paused the app, and "tor exited on its own" shows that it stopped tor.
- **Dial:** connects to the address in the text field, for example a `serve` running on the PC. To type an address into the field from the PC, tap the field and run `adb shell input text <address>`.
- **Stop** ends the running command. **Copy log** copies the log to the clipboard.

The app keeps `results.csv`, tor's logs and its own log (`console.txt`, saved after each command) in its private `files/run` folder. `adb` can read it through `run-as`, because the spike app is a debug build. To fetch them into `run/android`:

```
adb shell "run-as io.github.kaunazdagaz.p2pmessenger.torspike sh -c 'cd files/run && tar -cf - *.csv *.log *.txt' > /data/local/tmp/torspike.tar"
adb pull /data/local/tmp/torspike.tar spikes\Tor\run\android.tar
adb shell rm /data/local/tmp/torspike.tar
mkdir spikes\Tor\run\android -Force
tar -xf spikes\Tor\run\android.tar -C spikes\Tor\run\android
```

`adb logcat -s TorSpike` shows the log while it runs.

## What the spike writes to disk

In the state folder (`run/` by default): tor's data directories (directory cache, no onion keys), tor's logs (tor replaces addresses with `[scrubbed]`), `results.csv` (timings only), and `serve-onion-seed`, the unencrypted 32-byte seed of the `serve` instance's onion key. The seed is a throwaway test key; the app will keep its keys encrypted. The console output shows the onion addresses of the test services.

On the phone, everything stays in the app's private storage: tor's data directories, the key seed, the results, tor's logs and the app's log. The app's log, and with it logcat, shows the onion addresses of the test services.
