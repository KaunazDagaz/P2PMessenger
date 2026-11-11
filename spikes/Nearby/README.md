# Spike S05: the nearby route on a phone's hotspot

Spike code for step S05 of the development plan. It is never merged into `main`. It works on open decision 6 (guide Section 14): how the Noise KK responder learns who is calling, and how contacts find each other on one Wi-Fi network or one phone's hotspot without anyone typing an address. It has two parts:

1. **Design checks that need no network.** Caller identification with our own Noise KK (ADR 0003) in three ways, a private announcement format for discovery, the whole connection flow over loopback TCP, and a benchmark of what each way costs the responder.
2. **A probe node** for a laptop and for Android phones. It measures what a network lets through: UDP broadcast (directed and limited), UDP multicast, UDP unicast, mDNS/DNS-SD (Android's `NsdManager`, our own mDNS on the desktops), TCP in both directions, and the effect of the `MulticastLock`. It also shows whether the devices on a hotspot can reach each other (thesis risk RI-2).

| Path | Contents |
| --- | --- |
| `P2PMessenger.Spikes.Nearby` | Library: our Noise KK from S04 (`Noise/`), caller identification, announcements and the connection flow (`Identification/`), the probe node, mDNS and the reports (`Net/`), the checks (`Checks/`) |
| `P2PMessenger.Spikes.Nearby.Cli` | The `p2pm-nearby-spike` console app |
| `P2PMessenger.Spikes.Nearby.Android` | The probe node, the self-test and the benchmark as an Android app (section 6) |
| `Directory.Packages.props` | Adds the version of NSec.Cryptography to the repository's central package versions |

The projects are not part of `P2PMessenger.slnx`, so the architecture tests and CI do not see them. The only package is NSec.Cryptography 26.4.0, the same as in S04 (it brings libsodium 1.0.22). `run/` is ignored by Git. On `main` the repository's `.gitignore` hides the whole `spikes/` folder; on a spike branch, narrow that line the way the S04 branch does.

## Start here: what to do, in order

This is the short version for the author's equipment: **one phone and two laptops**. **Laptop 1** is the PC that holds this repository; **laptop 2** is the other laptop (assumed to be Windows on an Intel or AMD processor). Sections 1 to 3 below are done for you (self-test on Windows and Linux, the benchmark on the PC), so you do not repeat them. A **node** is the test program: you start it on every device at about the same time, and each one prints a table, the **matrix**, of which device heard which, by which way of sending. The sections after this one are the reference; you only need this part to run the test.

**Progress, 2026-11-11:** Part A is done (laptop 2, and the phone: the self-test passed 50 of 50 and the benchmark is in ADR 0004). Two runs on the phone's hotspot went well: every mechanism passed in every direction, and the two laptops reach each other through the phone (no client isolation). The second one was round 2 (MulticastLock unticked) and passed too. Mobile data was on in both runs and stays on, because you need the internet on laptop 2: round 1 with data off is **optional** now (ADR 0004, finding 14, explains from the phone's routing rules and a Linux rehearsal why the sharing phone should not care about its uplink). Round 3 (screen off) was repeated at 14:13 and passed: on battery, with the screen off for 5 min 40 s and Android's Doze on for 3 min 42 s, nothing stopped (the first attempt, at 13:52, was incomplete). Part B is done; only the optional rounds of Part C are left. The author accepted ADR 0004 on 2026-11-11 (option A4 and the discovery as proposed), so they block nothing.

How the results get to the agent: laptop 1 saves its log in `spikes\Nearby\run` by itself, and the agent reads it there. If the phone stays plugged into laptop 1 by USB during a round (it can: the hotspot works over Wi-Fi, and USB is only for `adb`; round 3 is the exception, see below), the agent fetches the phone's log itself (section 6), and you only say that the round is done. Otherwise press **Copy log** in the app, send the text to yourself (for example to Telegram's Saved Messages or by e-mail) and paste it into the chat. From laptop 2, copy its `node-laptop-2-<time>.txt` (in the `run` folder inside the folder where you started the program) to `spikes\Nearby\run` on laptop 1, or paste the text.

### Part A. Preparation (about 20 minutes, once)

1. **Laptop 2: copy the program.** On laptop 1 it is ready as `spikes\Nearby\run\nearby-win-x64.zip` (35 MB; the unzipped folder is `spikes\Nearby\run\win-x64`). Copy the zip to laptop 2 (USB stick, cloud, network share) and unzip it, for example into `C:\nearby`. If Windows SmartScreen says the app is from an unknown publisher, choose **More info**, then **Run anyway**: it is your own build. For a laptop that is not Windows on Intel or AMD, see "Another kind of laptop" in section 5.
2. **Laptop 2: check that it works.** Open PowerShell in that folder and run `.\p2pm-nearby-spike.exe selftest`: the last line must say `50 passed, 0 failed`. Then run `.\p2pm-nearby-spike.exe interfaces` and write down the name of the Wi-Fi adapter in the list (usually `Wi-Fi`).
3. **Phone: install the app.** Connect the phone to laptop 1 by USB, with USB debugging on. On laptop 1, in PowerShell from the repository root, run `dotnet build spikes\Nearby\P2PMessenger.Spikes.Nearby.Android -t:Run`. The app **Nearby spike** opens on the phone; allow notifications when it asks. If the build or the start fails, send the agent the error text.
4. **Phone: self-test and benchmark.** In the app press **Self-test**: the last line must say `50 passed, 0 failed`. Press **Bench** and wait until a table with `Contacts` in its header appears (up to a minute). Press **Copy log** and send the text to the agent. This shows that the code runs on the phone, and its numbers decide how callers are identified (ADR 0004).

### Part B. The hotspot test (the main part, about 1 hour)

**Before every round.** Phone: Settings, Display (or Display & touch), Screen timeout, 10 minutes, so that the screen does not go off by itself, and keep the app in front. Both laptops: plugged in, sleep set to never, lid open. On laptop 1 the program is `spikes\Nearby\run\win-x64\p2pm-nearby-spike.exe` (Windows already allows it on public networks); the commands below are for PowerShell in that folder on laptop 1 and in the unzipped folder on laptop 2.

**Round 1: baseline, mobile data off. Optional; skipped on 2026-11-11** (you need the internet on laptop 2, and your two runs had mobile data on). Do it only when you can spare a few minutes without internet on the laptops: it is the one case the phone itself has not shown (ADR 0004, finding 14). Both laptops are clients of the phone's hotspot.

1. Phone: switch mobile data, Wi-Fi and Bluetooth off, then switch the hotspot on (Settings, Network & internet, Hotspot & tethering; the path differs a little between phones). Check in the quick settings that mobile data really is off: on 2026-11-11 it was on in both runs (the app's first log lines showed `rmnet1` with an address and `Active network ... Cellular`; with data off those should be gone). With data off the phone has no default route, which is what this round tests. The band and the security type of the hotspot do not need writing down while the phone is plugged in: the agent reads them from the phone (on 2026-11-11: 2.4 GHz, WPA2/WPA3-Personal). If the hotspot will not start, or switches itself off, with mobile data off, switch mobile data on and tell the agent.
2. Both laptops: join the phone's hotspot. Windows will say that there is no internet; that is expected.
3. Both laptops: run `.\p2pm-nearby-spike.exe interfaces`. The adapter that joined the hotspot must show an address on the hotspot's small private network; the phone itself is the network's gateway (on 2026-11-11: laptops at `10.91.108.x`, the phone at `10.91.108.65`; `Get-NetIPConfiguration -InterfaceAlias Wi-Fi` shows the gateway). The first lines of the phone's log list the hotspot's own interface (`wlan0` on the Pixel 7a) with the same subnet.
4. Phone: swipe the app away and open it again, so that the log starts clean (the earlier runs are saved); its first lines list the hotspot's interface and subnet (**Interfaces** prints them again). Leave **Name** as it is (the phone's model), **Seconds** 150 and **Round trips** 100, and keep **MulticastLock** and **NsdManager** ticked.
5. Start the three nodes within about 20 seconds of each other, **with the phone last**, because each device's 150 seconds start when it starts (on 2026-11-11 the phone started a minute before the laptops, so all three overlapped for only 73 of 150 seconds). Type the node command in both laptop terminals without pressing Enter: on laptop 1 `.\p2pm-nearby-spike.exe node --name laptop-1 --duration 150 --iface Wi-Fi --mdns --pings 100`; on laptop 2 the same with `--name laptop-2`. Use the adapter's name from `interfaces` instead of `Wi-Fi` if it differs. Then press **Start node** on the phone and, right after, Enter in both terminals. The first time on a laptop (and for a program in a new folder), Windows asks about the firewall: tick **Private and Public** and press **Allow access** at once; until then the laptop answers nothing and the first TCP tests to it time out or take 3 seconds. **Do not start any other node on laptop 1 or laptop 2 during the round** (a second node on one computer shares the ports and appears in the tables as an extra device).
6. Wait 2.5 minutes. Every 15 seconds each device prints a table, and at the end a `final result` table (how to read it is below).
7. Tell the agent that round 1 is done (with the band and security type of the hotspot only if the phone was not plugged in). If the phone is plugged in by USB, that is all for the phone; if not, press **Copy log** and send the text. Send laptop 2's output too (or copy its log file, as above); the agent reads laptop 1's log itself. Then swipe the app away on the phone, so that the next round starts with a clean log.

**Round 2: without the MulticastLock. Done** on 2026-11-11 (with mobile data on): MulticastLock unticked, and everything still passed, multicast and mDNS included. It does not need repeating: the sharing phone does not need the lock (with the caveats in ADR 0004, finding 13: the system and Google Play services hold multicast locks of their own, and the lock concerns the phone's Wi-Fi client side, which is off while it shares). Whether a phone that is a Wi-Fi *client* needs the lock is round 4.

**Round 3: screen off. Done** on 2026-11-11 at 14:13 (second attempt; it passed: on battery, with the screen off for 5 min 40 s and Android's Doze on for 3 min 42 s, nothing stopped). The first attempt (13:52 to 14:03) did not test what it should: the phone's **Seconds** field was still 150, so its node ended after 150 s while the laptops ran 600; the phone stayed plugged in and charging; and the screen went off 88 s after the start, so it was off for only 62 s of the phone's run. The procedure (also for round 4c) is the same as round 1, but with mobile data **on** (as in your two runs, so that this round changes only the screen) and with 600 seconds: **Seconds** on the phone must show 600 (do not reopen the app between runs: it goes back to 150) and `--duration 600` on the laptops, and **unplug the phone from USB** before it starts (a charging phone does not go into Doze, so it would not show what a phone in a pocket does). Before pressing **Start node**, check that **Seconds** shows 600 and that the phone's status bar has no charging sign (a lightning bolt on the battery). About 30 seconds after the start, when a laptop's table shows the phone and the other laptop, press the phone's power button to turn its screen off. After 5 minutes wake the phone (do not open other apps) and wait until all nodes have finished, 10 minutes in all. The `Silences` section at the end shows whether anything stopped arriving while the screen was off. Send the logs the same way (plug the phone in again afterwards, or use **Copy log**).

### Part C. Optional

- **Round 4: the phone as a client.** The phone does not share; it and both laptops join an ordinary Wi-Fi network that you control (not a guest network, which often isolates its clients). Run it with the lock ticked (4a), with **MulticastLock** and **NsdManager** both unticked (4b), and with the lock ticked again and the screen off for 5 minutes (4c); 4d is 4a again with the router's cable to the internet unplugged and the phone's mobile data on (ADR 0004, finding 14). Details are in section 5. Skip it if you have no such network.
- **Round 5 (mobile data on while sharing) and round 6 (another phone, borrowed):** section 5.

### How to read the final table

An example of three devices that all hear each other (it comes from three Linux containers, not from a hotspot; the names are the ones you give with `--name`, and the phone's is its model):

```
--- final result: node laptop-1 after 00:45.0
  from -> to            bcast-dir  bcast-lim  mcast  uni  mdns  tcp
  --------------------  ---------  ---------  -----  ---  ----  -----------------------------------------------------
  laptop-1 -> laptop-2  45         45         45     44   1     ok connect 1 ms, rtt 0.6 ms (p95 1.1, max 3.4, n=100)
  laptop-1 -> phone     44         44         44     44   1     ok connect 1 ms, rtt 0.5 ms (p95 1.0, max 6.2, n=100)
  laptop-2 -> laptop-1  45         45         45     44   1     ok connect 0 ms, rtt 0.6 ms (p95 0.9, max 3.3, n=100)
  ...
Verdict per mechanism (a pair counts as working when at least one datagram or one TCP test went through):
  bcast-dir 6 of 6 directions work
  ...
Silences of 4 s or more between datagrams from one device and mechanism ...:
  none
```

- One row per direction: `laptop-1 -> phone` is what the phone heard from laptop-1. A number is how many probes the receiver heard; there is about one per second, so about 150 in a 150-second round. `-` means the receiver heard nothing; `?` means that receiver's own report has not reached this device yet.
- The columns are the ways of sending: `bcast-dir` (broadcast to the network's own broadcast address), `bcast-lim` (broadcast to 255.255.255.255), `mcast` (multicast), `uni` (direct, unicast), `mdns` (the service-discovery standard; one find is enough, so the number is 1) and `tcp` (a real connection with 100 round trips: `ok` with the times, or the reason it failed).
- What the test wants to know: which ways reach between each pair, the phone and its clients included; whether the two laptops reach each other through the phone (if not, the line `NO PATH between laptop-1 and laptop-2 by any mechanism: client isolation on the hotspot or a firewall is likely` appears, and that is the answer for thesis risk RI-2); whether `mcast` and `mdns` to the phone still arrive with the MulticastLock off; and whether `Silences` lists long gaps after the screen was off.
- A way that does not work is not a failed test; it is a result, and the agent explains it. A failed test is one where a program did not run or a device heard nobody.
- A `tcp` cell such as `ok ConnectionRefused; 6 failed` means that it worked earlier and the latest tries failed; the usual reason is that the other device's node has already ended (the nodes run 150 seconds each, from their own start). The status tables above it in the log keep the earlier numbers.
- A row for a device you did not start (on 2026-11-11 a test node of the agent appeared as `smoke2`) means that another node ran on one of the networks or computers; it adds a ghost row to every table and doubles the `uni` counts toward its computer. Ignore the rows, or stop the node and repeat the round.

### If something does not work

| What you see | What to do |
| --- | --- |
| `No usable IPv4 interface` on a laptop | It is not on the hotspot, or `--iface` does not match the adapter's name: run `interfaces` and use the name it shows |
| `No other node was heard by any mechanism` on a device | The other devices are not running yet or are on another network, or this device's firewall blocks it: check the address from `interfaces`, allow the firewall prompt (Private and Public) and start all three again |
| `tcp` shows `no answer within 5 s (timeout)` towards one laptop only | That laptop's firewall (allow Public networks) or Wi-Fi power saving: fix it and repeat the round |
| `NO PATH between laptop-1 and laptop-2` while both reach the phone | Not something to fix: the phone isolates its clients. Tell the agent the phone's model and Android version |
| `The UDP port 47801 cannot be opened` | Another node still runs on that computer: close it (Ctrl+C) and start again |
| The phone app closes or shows an error | Send the agent the output of `& "$env:ANDROID_HOME\platform-tools\adb.exe" logcat -d -s NearbySpike AndroidRuntime` (adb is not on this PC's PATH, hence the full path) |
| `error XA0010: No available device` when you install the app | The build does not see the phone. Run `& "$env:ANDROID_HOME\platform-tools\adb.exe" devices`: the phone must be listed as `device`. If it is `unauthorized`, unlock the phone and accept the USB debugging prompt; if it is not listed, set the USB mode to File transfer, replug the cable, and run the build again |

## 1. Self-test

Windows, from the repository root:

```
dotnet run --project spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -- selftest
```

Linux: `dotnet run --project spikes/Nearby/P2PMessenger.Spikes.Nearby.Cli -- selftest`. It prints one line per check and writes the same output to `spikes/Nearby/run/selftest-<time>.txt`. Expected result: `0 failed`. It takes a few seconds and needs no network.

**Linux without a Linux machine or WSL: Docker Desktop** (the way S04 was checked). From the repository root, in PowerShell:

```
docker run --rm -v "${PWD}:/repo:ro" -v "${PWD}\spikes\Nearby\run\docker:/out" mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'mkdir /work && tar -C /repo --exclude=.git --exclude=bin --exclude=obj --exclude=./spikes/Nearby/run -cf - . | tar -C /work -xf - && cd /work && curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --jsonfile global.json --install-dir /root/.dotnet && export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 && dotnet --version && dotnet run --project spikes/Nearby/P2PMessenger.Spikes.Nearby.Cli -- selftest; dotnet run --project spikes/Nearby/P2PMessenger.Spikes.Nearby.Cli -c Release -- bench-caller; cp spikes/Nearby/run/*.txt /out/'
```

It mounts the repository read-only and copies it into the container without `.git`, `bin` and `obj` (they are not needed and hold over a gigabyte here), installs the SDK that `global.json` pins (the image's own SDK was older on 2026-11-11; the download is 240 MB), builds on Linux with the repository's analyzers and warnings as errors, runs the self-test and the benchmark, and copies both logs to `spikes\Nearby\run\docker`. It takes 2 to 5 minutes. Expected: `50 passed, 0 failed` and a benchmark table. If the SDK download fails, the installer retries for several minutes before it gives up: stop it with Ctrl+C and run the command again. The command is for PowerShell; `cmd` quotes differently.

If the download keeps failing (it did on a poor network: the connection broke partway through the 240 MB, and the installer starts again from zero each time), download the SDK once on Windows, where `curl.exe` resumes a broken download, and mount the file (the version in the address is the one in `global.json`):

```
curl.exe -L -C - --retry 10 --retry-all-errors -o spikes\Nearby\run\dotnet-sdk.tar.gz https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-linux-x64.tar.gz
docker run --rm -v "${PWD}:/repo:ro" -v "${PWD}\spikes\Nearby\run\docker:/out" -v "${PWD}\spikes\Nearby\run\dotnet-sdk.tar.gz:/sdk.tar.gz:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'mkdir -p /work /root/.dotnet && tar -C /repo --exclude=.git --exclude=bin --exclude=obj --exclude=./spikes/Nearby/run -cf - . | tar -C /work -xf - && tar -xzf /sdk.tar.gz -C /root/.dotnet && cd /work && export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 && dotnet --version && dotnet run --project spikes/Nearby/P2PMessenger.Spikes.Nearby.Cli -- selftest; dotnet run --project spikes/Nearby/P2PMessenger.Spikes.Nearby.Cli -c Release -- bench-caller; cp spikes/Nearby/run/*.txt /out/'
```

That run takes about a minute, because the SDK is already on disk. Delete `spikes\Nearby\run\dotnet-sdk.tar.gz` (230 MB) when you no longer need it.

| Section | What it checks |
| --- | --- |
| Caller identification by trying each contact's key | A real KK message 1 from each of 6 contacts is attributed to the right contact, by the naive version (a full message 1 read per contact) and by the cached version (our own reader with the static-static key agreements kept per contact); a stranger and a contact with a changed key are refused after every contact was tried; another prologue, every single-bit change, short and long messages are refused |
| By a pairwise hint | A 32-byte preamble (16 bytes nonce, 16 bytes keyed BLAKE2b tag of the pairwise key) names the caller, nothing else matches, a stranger is refused, every single-bit change is refused, two hints of one pair share nothing; a recorded hint is accepted again (replay, noted) |
| By a pairwise rotating tag | The same with a tag derived from the pairwise key and a 600-second epoch: found in the epochs before, at and after the receiver's, refused two epochs away |
| Private announcement | A 274-byte datagram (16 bytes nonce, 2 bytes port, 32 slots of 8 bytes, unused slots random) that each chosen recipient recognizes and nobody else; the same size for 1, 5 and 32 recipients; no name or key in the bytes; every single-bit change (2,192 copies) either makes exactly one recipient refuse it or has no effect; 20,000 random datagrams recognized by nobody; 70 contacts need 3 announcements. Run without a clock and with an epoch |
| Probe format, interface arithmetic, reports, mDNS codec | The probe datagram, the broadcast address of an interface, how the matrix and its verdicts read (including "no path between two clients" and "no report yet"), the DNS codec against compressed names, loops and bit flips |
| Connection flow over TCP on loopback | Hint, KK handshake and messages between two peers; a stranger and a contact with a changed key are closed on without a byte sent back; a replayed first flight draws message 2 but is never confirmed; time to connect |

## 2. Benchmark of caller identification

```
dotnet run --project spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -c Release -- bench-caller
```

It prints, for 1, 10, 50, 100 and 500 contacts, the median time in microseconds that the responder spends on one inbound attempt, by each way of identifying the caller, for a known caller (in the middle of the list) and for an unknown one (every contact is tried), and what an announcement from nobody costs. `bench-caller 20 200` takes other contact counts. Use a Release build. The Docker command of section 1 runs it on Linux too. The Android app has the same as **Bench**; a phone's numbers are what the ADR needs most.

## 3. Interfaces

```
dotnet run --project spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -- interfaces
```

Lists the IPv4 interfaces that are up, with network, broadcast address and host count; the node uses the ones marked `*`. Loopback, link-local (169.254.x.x), very large or small subnets and cellular or tunnel interfaces (names starting `rmnet`, `ccmni`, `v4-`, `clat`, `pdp`, `ppp`, `tun` or `wwan`) are skipped, so a phone with mobile data on or a VPN does not probe those networks. On a laptop on a phone's hotspot, the Wi-Fi adapter shows the hotspot's subnet (for example 192.168.43.0/24).

## 4. The probe node

```
dotnet run --project spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -- node --name laptop --duration 120 --iface Wi-Fi --mdns --pings 100
```

Start one on every device at about the same time. Each node:

- listens on UDP port 47801 (probes), TCP port 47800 (echo server) and, with `--mdns`, UDP port 5353;
- every second, on every interface, sends a probe by directed broadcast (`bcast-dir`, for example 192.168.43.255), limited broadcast (`bcast-lim`, 255.255.255.255), multicast (`mcast`, group 239.255.83.5, TTL 1) and unicast (`uni`, to every peer it knows; every 15 seconds also to every address of an interface with at most 510 hosts, a "sweep"), always from a socket bound to that interface (the sharing phone may have no default route, and a socket without an interface could then fail). The sweep runs in the background on a socket of its own: sending to hundreds of addresses where nobody answers took 3 to 9 seconds on a Docker network (the sends wait for address resolution and fill the socket's send buffer), and it must not hold up the regular probes. The log says `Sweep on <interface>: ... took ... ms`;
- advertises and browses the service `_p2pms05._tcp` over mDNS (`mdns`; on Android with `NsdManager`, on the desktops with a small implementation of its own);
- dials every peer it learns about, over TCP, and runs `--pings` round trips (`tcp`);
- puts what it has heard into every probe it sends, so any one device's screen shows the whole matrix once some path works between the devices.

Every 15 seconds and at the end it prints the matrix: one row per direction (sender -> receiver), one column per mechanism. A number is how many datagrams the **receiver** heard by that mechanism; `tcp` shows the **dialer's** result (connect time, median, 95th percentile and slowest round trip, or the failure: `ConnectionRefused` when the device answered that nothing listens, `no answer within 5 s (timeout)` when packets vanish, which is what client isolation and a closed firewall look like). `-` means the observer heard nothing; `?` means the observer's own report has not reached this device. A verdict per mechanism follows, and `NO PATH between X and Y` when the device knows both and nothing went between them. After the matrix come the send counts and errors per interface and mechanism (`NetworkUnreachable` on `bcast-lim` from the sharing phone is the kind of finding to look for), the multicast group joins, how datagrams arrived (`unicast`, `multicast`, `directed-bcast` or `limited-bcast`, as the operating system reports the destination address), and the longest silences per sender and mechanism, which show a stall such as multicast stopping with the screen off.

Options: `--name`, `--duration <seconds, 0 = until Ctrl+C>`, `--iface <part of a name or an address>` (repeatable), `--peer <ip>` (when nothing finds the peer), `--tcp-port`, `--udp-port`, `--group`, `--interval <ms>`, `--sweep-max-hosts`, `--pings`, `--mdns`. The `mdns` column counts how often a device found the other's service (once per peer, the node reports a service once).

Two nodes on one machine share the UDP port, so unicast probes reach only one of them; ignore the `uni` column for such a test, and give the second node `--tcp-port 47810`.

## 5. The hotspot test (one phone and two laptops)

In rounds 1 to 3 the phone shares the hotspot and the two laptops join it as its clients. That measures the paths the plan asks for with this equipment: phone to laptop and laptop to phone (the sharing phone sends and receives), and laptop to laptop through the phone (client isolation, RI-2). In round 4 the phone is a client on an ordinary Wi-Fi network. Not covered until a phone is borrowed (round 6): phone to phone and other phone models. The short, step-by-step version of rounds 1 to 3 is at the top of this file ("Start here"); this section is the reference.

Needs: the phone with USB debugging and the app from section 6, two laptops with Wi-Fi, and for round 4 a Wi-Fi network that you control. Use only networks of your own: the probes carry the device names you give, and the node's log shows IP addresses.

**Preparation, once.** Both laptops run the same published build, so laptop 2 needs neither the SDK nor the repository. On laptop 1, from the repository root:

```
dotnet publish spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -c Release -r win-x64 --self-contained -o spikes\Nearby\run\win-x64
```

Copy the folder `spikes\Nearby\run\win-x64` (about 80 MB) to laptop 2, by USB stick or zipped. On each laptop open a terminal in that folder and run `.\p2pm-nearby-spike.exe selftest` (expected: `0 failed`) and `.\p2pm-nearby-spike.exe interfaces` (it shows the Wi-Fi adapter's name for `--iface`; on a non-English Windows it may not be `Wi-Fi`). The build on laptop 1 writes its logs to `spikes\Nearby\run`; on laptop 2 they go to a `run` folder inside the folder it is started from, and after each round you copy them to `spikes\Nearby\run` on laptop 1.

Another kind of laptop: publish with `-r win-arm64`, `linux-x64` or `linux-arm64` and its own `-o` folder (for example `spikes\Nearby\run\linux-x64`); Linux has no `.exe`, and the copied file needs `chmod +x p2pm-nearby-spike`. macOS was not tried. A publish with `-r` rewrites the `packages.lock.json` files of the spike; the next normal `dotnet build` puts them back.

Before each round, plug both laptops in, keep their lids open and set sleep to never for the test: a sleeping laptop, or a Wi-Fi adapter that saves power, looks like a stall in the log and says nothing about the phone. On the first run Windows asks to allow the app through its firewall: allow private **and public** networks (a hotspot counts as public), on both laptops (section 8 has the command line way).

Every round uses the same three commands, started within about 20 seconds of each other, the phone first:

- **Phone:** open the app and leave **Name** as it is (the phone's model, which then shows in the matrix); keep **MulticastLock** and **NsdManager** ticked unless the round says otherwise; the app starts with 150 seconds and 100 round trips, which are right for rounds 1 and 2; press **Start node** and allow the notification when Android asks.
- **Laptop 1:** `.\p2pm-nearby-spike.exe node --name laptop-1 --duration 150 --iface Wi-Fi --mdns --pings 100`
- **Laptop 2:** `.\p2pm-nearby-spike.exe node --name laptop-2 --duration 150 --iface Wi-Fi --mdns --pings 100`

After 2.5 minutes read the matrix on any device; with three devices it has six rows. Keep the logs: the phone's (**Copy log**, or the `adb` commands of section 6), laptop 1's `node-laptop-1-<time>.txt` and laptop 2's `node-laptop-2-<time>.txt`, all in `spikes\Nearby\run` on laptop 1, where the agent can read them. Note the phone's model and Android version, the band and security of the hotspot, and anything unusual.

**Round 1, baseline: the phone shares.** On the phone: mobile data off, Wi-Fi off, Bluetooth off, then the hotspot on (note the band, 2.4 or 5 GHz, and the security setting). Join both laptops to it; their other adapters, such as Ethernet or a VPN, may stay on, because `--iface` keeps the node to the hotspot's adapter. Press **Interfaces** in the app to see the hotspot's interface and subnet, then start the three nodes as above. With the data off this round is optional: it was skipped on 2026-11-11 (ADR 0004, finding 14), and the two runs so far had mobile data on.

**Round 2, without the MulticastLock.** The same, with **MulticastLock** unticked on the phone. This shows whether the sharing phone receives multicast and mDNS without it. Done on 2026-11-11 (with mobile data on): everything passed. The result is not a clean test of the lock, because other holders of multicast locks exist on the phone (`dumpsys wifi` lists them under `Multicast Locks held`; on 2026-11-11 they were the system's `NsdService` and Google Play services), and because the lock acts on the phone's Wi-Fi client side, which is off while it shares; ADR 0004, finding 13.

**Round 3, screen off.** The same with 600 seconds on all three. Once the matrix shows all the devices, turn the phone's screen off for 5 minutes, then wake it. The silences at the end show whether anything stopped arriving at the phone, or from the phone, while its screen was off. (Whether the foreground service keeps the app alive is S06's question; here only the network is looked at.)

**Round 4, the phone as a client.** The phone does not share. It and both laptops join a Wi-Fi network that you control: the main network of your router, not its guest network, because guest networks often isolate their clients. The laptops may use Ethernet instead (`--iface` with the adapter's name from `interfaces`). Mobile data may stay on; the app leaves the cellular interface alone. Without a router, switch on the **Mobile hotspot** of one laptop (Windows Settings, Network & internet; it may want an internet connection to share) and join the phone and the other laptop to it; the laptop that shares takes the router's part, its node uses `--iface 192.168.137.1` (the usual address of that hotspot; `interfaces` shows it), and the phone's mobile data is off. Three runs, and an optional fourth:

- **4a** as round 1, with **MulticastLock** ticked;
- **4b** the same with **MulticastLock** and **NsdManager** both unticked, so that the app asks for no multicast lock itself (the agent reads `dumpsys wifi` for the locks that others hold);
- **4c** with it ticked again, 600 seconds, and the phone's screen off for 5 minutes as in round 3;
- **4d** (optional) as 4a, with the router's cable to the internet unplugged and the phone's **mobile data on**: the phone's Wi-Fi then has no internet, and Android may keep the cellular network as the phone's default one, which is the case where mobile data can matter (ADR 0004, finding 14). The laptops need no internet for it.

**Round 5 (optional), mobile data on.** Round 1 again with mobile data on in the sharing phone, which is the usual state of a phone that shares its connection. A phone with data on may route differently; the send errors per interface in the log show it, and the app's interface list shows the names of the cellular interfaces it skips. Done by chance on 2026-11-11 (round 1 with the data left on) and again in round 2: everything passed both times, so it does not need repeating.

**Round 6 (optional), another phone model (risk RI-2).** Borrow a phone, put the app on it (section 6) and repeat round 1 with it as the sharing phone and the two laptops as clients. Note the make, model and Android version, and any client-isolation setting the phone has. Without this round, R2 stays open for other models.

What an outcome tells the decision:

| Matrix | Meaning for the nearby route |
| --- | --- |
| `bcast-dir` works in every direction, including to and from the sharing phone | A broadcast announcement on the interface's own broadcast address is enough |
| `mcast` works only with the MulticastLock (round 2 against round 1, or 4b against 4a), or not to the sharing phone | The app needs the lock (S19) and may need broadcast as well |
| `bcast-*` and `mcast` fail, but `uni` and `tcp` work | Discovery must sweep the subnet with unicast announcements; `NO PATH` does not appear |
| `mdns` works where `mcast` does not | `NsdManager` has a path that plain multicast lacks on this model: add it on Android |
| Everything fails between laptop-1 and laptop-2, but both reach the phone (`NO PATH between laptop-1 and laptop-2`) | The phone isolates its clients (RI-2): list the model; clients cannot message each other there. The same pair works on the router (round 4), which shows that it is the phone's hotspot and not the laptops |
| Only one laptop hears nothing, or `tcp` towards it times out, while the other laptop is fine | That laptop's firewall or Wi-Fi power saving, not the phone: fix it and repeat the round |
| `tcp` fails with `ConnectionRefused` | The device is reachable and nothing listens: start the node there |
| `tcp` fails with a timeout while `uni` works or the other direction works | A firewall on the receiving side (Windows: allow public networks) |
| Silences of tens of seconds from or to the phone in round 3 or 4c | Something stops while the screen is off (Wi-Fi power saving, multicast filtering): note which mechanisms stall and which carry on |

## 6. Android test app

`P2PMessenger.Spikes.Nearby.Android` is a plain .NET for Android app, without Avalonia, that runs the node, the self-test and the benchmark on a phone. It needs Android 8.0 (API 26) or later, the android workload, the Android SDK and a JDK (see the repository README), and a phone with USB debugging. It declares `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`, the foreground service permissions and `POST_NOTIFICATIONS`; it does not declare `ACCESS_LOCAL_NETWORK`, because the app targets API 36 (guide Section 4.5).

```
dotnet build spikes\Nearby\P2PMessenger.Spikes.Nearby.Android -t:Run
```

With the phone connected, that is all. If more than one device is attached (`adb devices` lists them), pick the phone with `-p:AdbTarget="-s <serial>"`. `adb` is in `C:\Android\sdk\platform-tools` and is not on the PATH of this PC; in PowerShell define it once with `$adb = "$env:ANDROID_HOME\platform-tools\adb.exe"` and call it as `& $adb devices`. For round 6, turn on USB debugging on the borrowed phone and deploy to it the same way: a Debug build keeps its assemblies outside the APK, so the APK file alone does not run on another phone. The top of the log shows the device, Android version, page size, the interface list read through .NET and through Java (`NetworkInterface` has failed on some Android versions in .NET; the app uses Java's list when it works), and the active network. The fields and buttons:

- **Name** of the device in the matrix; **Peer IP** to dial when nothing finds the peer; **Seconds** to run (0 = until Stop; starts at 150); **Round trips** per TCP test (starts at 100).
- **MulticastLock** and **NsdManager** switch the two Android helpers on and off for the run.
- **Start node** runs the node in a foreground service (so it keeps running with the screen off); **Stop** ends it; **Copy log** copies the whole log, everything since the app was opened (swipe the app away between rounds to start a clean log).
- **Interfaces** prints the top of the log again (start the hotspot first, then press it); **Self-test** and **Bench** are sections 1 and 2 on the phone (a Debug build, so slower than a release build).

`& $adb logcat -s NearbySpike` shows the log while it runs. The app saves each run's log and `nearby-results-<name>.csv` in its private `files/run` folder; it is a debug build, so `adb` can fetch them through `run-as` (tried on the Pixel 7a on 2026-11-11; if it fails, use **Copy log**):

```
& $adb shell "run-as io.github.kaunazdagaz.p2pmessenger.nearbyspike sh -c 'cd files/run && tar -cf - .' > /data/local/tmp/nearbyspike.tar"
& $adb pull /data/local/tmp/nearbyspike.tar spikes\Nearby\run\android.tar
& $adb shell rm /data/local/tmp/nearbyspike.tar
mkdir spikes\Nearby\run\android -Force
tar -xf spikes\Nearby\run\android.tar -C spikes\Nearby\run\android
```

The app compiles with the repository's analyzers and warnings as errors. It first ran on a phone on 2026-11-11 (the author's Pixel 7a, Android 17): the self-test, the benchmark and, the same day, the node on the phone's hotspot (round 5 in the report).

## 7. Rehearsal without phones

The probe node was rehearsed on this PC: two nodes on Windows (on the PC's virtual `vEthernet` adapter only, so nothing went onto the Wi-Fi network), and three Linux containers on a Docker bridge network, which is a real broadcast domain with real multicast. A container network is not a hotspot: it shows that the code works, not what phones allow. (The self-test and the benchmark on Linux are section 1.)

```
dotnet publish spikes\Nearby\P2PMessenger.Spikes.Nearby.Cli -c Release -o %TEMP%\nearby-publish
docker network create --driver bridge --subnet 172.30.0.0/24 s05net
docker run -d --name s05-a --network s05net -v %TEMP%\nearby-publish:/app:ro -v <repo>\spikes\Nearby\run\docker:/work/run -w /work mcr.microsoft.com/dotnet/sdk:10.0 dotnet /app/p2pm-nearby-spike.dll node --name lin-a --duration 60 --mdns
```

The same with `lin-b` and `lin-c`, then `docker logs s05-a`. To rehearse a network without a default route, do not use `docker network create --internal`: on 2026-11-11 limited broadcast, multicast and mDNS never arrived there, while an ordinary bridge with the default route deleted inside each container passed everything. The SDK image has no `ip`, so install it once (`docker run --name s05-ipinst mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'apt-get update && apt-get install -y iproute2'`, then `docker commit s05-ipinst s05-ip`) and start each node with `--cap-add NET_ADMIN` and `sh -c 'ip route del default; dotnet /app/p2pm-nearby-spike.dll node ...'` on the image `s05-ip`. Python's `zeroconf` was used as an independent mDNS peer for the interoperability check of section 4 (its licence is in `docs/third-party-notices.md`).

## 8. Firewalls

Windows blocks incoming connections and datagrams from other devices until the app is allowed: on the first run it asks, and on a hotspot the network counts as public. If the prompt did not appear or was dismissed, allow the program from an elevated terminal and delete the rule after the tests:

```
netsh advfirewall firewall add rule name="p2pm-nearby-spike" dir=in action=allow program="<full path of p2pm-nearby-spike.exe>" profile=private,public
netsh advfirewall firewall delete rule name="p2pm-nearby-spike"
```

Linux machines with `ufw` or `firewalld` need UDP 47801 and 5353 and TCP 47800 opened. A blocked laptop shows up as `tcp` failing with a timeout towards it and no datagrams heard by it.

## What the spike writes to disk

Logs and `nearby-results-<name>.csv` in `run/` (on the phone, in the app's private `files/run`). The test identities of the design checks are derived from fixed names and are not secrets; no key is printed. The probe datagrams carry the device names you give, the TCP echo carries nothing else, and the node's log shows IP addresses, which a spike needs to be useful; the app itself will not log addresses (guide Section 6.7).
