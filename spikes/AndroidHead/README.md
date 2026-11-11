# Spike S06: the Avalonia Android head, the foreground service and QR scanning

Spike code for step S06 of the development plan. It is never merged into `main`. It tests what the Android head of the messenger has to settle before S07 re-plans:

- an Avalonia 12 app on Android: `AvaloniaMainActivity`, an `AvaloniaAndroidApplication<TApp>` class and `IActivityApplicationLifetime`;
- the type of the foreground service that keeps the listener alive (open decision 4, guide Section 14: `connectedDevice` or `specialUse`), the persistent notification with a quit action, `POST_NOTIFICATIONS`, and the battery-optimisation request;
- whether a TCP listener in that service stays reachable with the screen off (a rough NFR-05 check);
- QR scanning: CameraX and ZXing.Net on Android, FlashCap on a desktop, and whether the camera is shown in an Avalonia view or in a separate activity;
- tor (ADR 0002, option C) started from the foreground service on Android 8.0, 12, a 16 KB-page image and the author's phone.

| Path | Contents |
| --- | --- |
| `P2PMessenger.Spikes.AndroidHead` | Library: the probe protocol and listener (`Probe/`), QR decoding with ZXing.Net, QR encoding, a PNG writer and a text renderer (`Qr/`) |
| `P2PMessenger.Spikes.AndroidHead.Cli` | The `p2pm-androidhead-spike` console app for the PC: `selftest`, `probe`, `qr`, `camera`, `apk-libs` |
| `P2PMessenger.Spikes.AndroidHead.Android` | The Avalonia app for the phone: the foreground service, the permission requests, both camera views, tor, and `adb` controls |
| `Tor/` | S03's tor library and commands (`selftest`, `pair`, `serve`, `dial`), compiled into the app as they are |
| `get-tor.ps1` | S03's script that downloads and verifies the tor bundle into `tor-bundles/` (ignored by Git); its default server is now the Tor Project's archive, because the main server dropped bundle 16.0 |
| `Directory.Packages.props` | Adds ZXing.Net 0.16.11, FlashCap 1.13.0 and the CameraX bindings 1.6.2 to the repository's central package versions |

The projects are not part of `P2PMessenger.slnx`, so the architecture tests and CI do not see them. `run/` and `tor-bundles/` are ignored by Git. Nothing here uses Google Play services.

## Start here: what to do, in order

The short version for the author's equipment: **one phone** (the Pixel 7a, Android 17) and **two laptops**. **Laptop 1** is the PC that holds this repository; **laptop 2** is the other one. `adb` is not on the PATH of laptop 1; in PowerShell define it once with `$adb = "$env:ANDROID_HOME\platform-tools\adb.exe"` and call it as `& $adb devices`.

**Progress, 2026-11-11:** Parts A, B and C are done. Done on the phone: steps 1 to 6 and step 8, and the notification's Quit. Done on laptop 2 (Part C): the webcam read the QR code on the first frame. Step 7 was run three times. The first, on USB with "unplugged" simulated, answered three probes of four after the screen had been off for 29 to 45 minutes, and the first one, at 29 minutes, timed out because the phone did not answer the PC's ARP request. The second, with the cable really out, was void: the phone was sharing its hotspot and the probe was aimed at its old Wi-Fi address. The third, with the cable really out, the phone a Wi-Fi client and laptop 1 on the same network (20:12 to 21:02), was valid, and none of its four probes, 27 to 43 minutes after the screen went off, was answered; the phone answered again after its screen came on (S06 report, finding 4). Not run, by the author's choice and left to S19: step 7 again with the battery exemption allowed and with **Wake lock** ticked (step 7.6).

### Part A. Preparation (about 10 minutes, once)

1. **Tor bundle** (done on laptop 1; needs `gpg` from Git for Windows): `powershell -ExecutionPolicy Bypass -File spikes\AndroidHead\get-tor.ps1 -Platform android-aarch64,android-x86_64`. It downloads the Tor Expert Bundle 16.0, checks the Tor Project's signature and unpacks it into `spikes\AndroidHead\tor-bundles`.
2. **The PC tool:** `dotnet run --project spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Cli -- selftest`. The last line must say `49 passed, 0 failed`. It needs no camera and no network.
3. **The app on the phone.** Connect the phone by USB with USB debugging on. A ready Debug APK (88 MB, arm64 and x86_64, assemblies inside) is `spikes\AndroidHead\run\androidhead-debug-dual.apk`: `& $adb install -r spikes\AndroidHead\run\androidhead-debug-dual.apk`. Add `-g` to grant the notification and camera permissions at install (then the system dialogs do not appear; leave `-g` out when you want to see them). To build it yourself: `dotnet build spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Android -p:EmbedAssembliesIntoApk=true`; the APK is in `bin\Debug\net10.0-android36.1\` as `...-Signed.apk` (the first build needs the tor bundle from step 1). `dotnet build spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Android -t:Run` installs and starts the app with fast deployment, which keeps the assemblies outside the APK, so that APK does not run on another phone; with more than one device attached (`& $adb devices`), pick one with `-p:AdbTarget="-s <serial>"`.

### Part B. The phone (about 1 hour; one run of 45 minutes is passive)

Open the app **Android head spike**. The buttons act on the log at the bottom, which the app also writes to Android's log (`& $adb logcat -s AndroidHeadSpike`). **Copy log** copies the whole log to the clipboard.

1. **Info.** Shows the device, the Android version, the page size, whether the notification and camera permissions are granted, the native libraries with their file modes (`libTor.so` must be there, mode 755), the phone's IPv4 addresses and the active network.
2. **Notifications.** Allow. Without the permission the service still runs, but its notification is hidden (Android 13 and later).
3. **Foreground service types.** Choose **ConnectedDevice** in the list, press **Start service**: the log must say `StartForeground accepted (ConnectedDevice); the system reports the type as TypeConnectedDevice`, and a notification with a **Quit** button appears. The line `TCP listener on port 45821` lists the phone's addresses. Press **Stop service**, choose **SpecialUse** and repeat. **Untyped** starts the service without a type; on Android 14 and later the system then uses the types declared in the manifest. Tapping **Quit** in the notification must close the app completely (US-18 AC4).
4. **Battery exemption.** Press it: Android asks "Let app always run in background?". Press **Deny** and check that **Start service** still works (US-18 AC3), then press the button again and **Allow**. The log says what Android answered and whether the app is exempt now; on Android 17 the answer is `Canceled` after both buttons, so read the "exempt" part. To take the exemption away again: Settings, Apps, the app, App battery usage, Optimised (the path differs a little between phones), or `& $adb shell dumpsys deviceidle whitelist "-io.github.kaunazdagaz.p2pmessenger.androidheadspike"`.
5. **Camera.** Press **Camera permission** (allow). On laptop 1 make a QR code and show it full screen: `dotnet run --project spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Cli -- qr "p2pm-scan-check-0001" --png spikes\AndroidHead\run\qr-poster.png --scale 16 --canvas 1024x1024`, then open the PNG. **Scan in activity** opens a separate camera screen and logs `Scan activity returned 20 characters: <text>`; **Scan in view** shows the camera inside the Avalonia screen and logs `embedded scanner: 20 characters: <text>`, with an Avalonia label drawn over the lower part of the camera view. On the Pixel the label is not visible (the native preview covers it) and both read the code in under 2 seconds. Try both with the code upright, turned and as light modules on a dark background (`qr ... --dark` in a terminal with a dark background). The log shows the frame size, the first-frame time and the decode time per frame.
6. **Tor.** **Tor self-test** (12 checks) and then **Tor pair**: two tor instances in the app, each publishing an onion service and connecting to the other, then reconnecting five times over new circuits; it takes a few minutes, and the table at the end has the bootstrap, descriptor upload and connect times. Needs the internet. **Tor serve** publishes one onion service and prints its address; `dial <address>` from S03's `p2pm-tor-spike` on a PC connects to it.
7. **30 minutes with the screen off (the rough NFR-05 check).** Laptop 1 and the phone must be on one network, and the run measures the case you pick: either the phone is a Wi-Fi client of an ordinary Wi-Fi network (turn its hotspot off) and laptop 1 is on that network, or the phone shares its hotspot (mobile data on) and laptop 1 is on the hotspot. The two cases can differ, so say which one you ran.
   1. Unplug the phone (a charging phone does not go into Doze, and a phone on a USB cable keeps its processor awake: the heartbeat then says `the processor slept 0.0 s`, which tells you which case you have), choose **ConnectedDevice**, leave **Wake lock** unticked and press **Start service** while the phone is unlocked and the app is in front (Android refuses to start a service from behind the lock screen). Read the phone's address (`wlan0 ...`) in the log line `TCP listener on port 45821`; take it from this log and not from an earlier run, because it changes with the network.
   2. **Check that laptop 1 reaches the phone before the long wait:** `dotnet run --project spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Cli -- probe <address>`. It must print `ok` within a second or two. If it times out (after about 21 seconds), laptop 1 is not on the phone's network: fix that first, and do not start the long run. A 45-minute run aimed at a wrong address measures nothing (it happened on 2026-11-11).
   3. On laptop 1: `dotnet run --project spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Cli -- probe <address> --delay 1740 --every 300 --count 4 --timeout 60`. It probes about 29, 34, 39 and 44 minutes after the start (each wait counts from the end of the previous probe) and prints, for each, the connect time, the reply time and the phone's state (screen, Doze, power saver, charging, exempt, service type, wake lock).
   4. Press the phone's power button at once and leave it alone for 45 minutes. A reply with `screen=off` and `idle=deep` or `idle=light` is the case that matters; a reply later than 60 seconds counts as late (NFR-05).
   5. Afterwards press **Copy log**, or fetch the service's file (below). Every minute the service logs a heartbeat line with how late its own timer fired and how long the processor slept; those two numbers show what Doze did to timers (S19).
   6. Optional second run with **Wake lock** ticked, to see whether it changes anything.
8. **Swipe from the recent apps.** With the service running, open the recent apps and swipe the app away. Does the notification stay, is `probe <address>` still answered, and does the log (from the service file) have the line `the task was swiped away from the recent apps`? Some phone makers kill the process of a swiped app; this checks the Pixel.

### Part C. Laptop 2: the webcam (about 10 minutes)

FlashCap is the desktop camera library; this checks it on a real webcam (US-03 AC1). A ready self-contained Windows build (35 MB) is `spikes\AndroidHead\run\androidhead-win-x64.zip` on laptop 1 (to build it again: `dotnet publish spikes\AndroidHead\P2PMessenger.Spikes.AndroidHead.Cli -c Release -r win-x64 --self-contained -o spikes\AndroidHead\run\win-x64`, then zip the folder). Copy the zip to the Desktop of laptop 2 and run in PowerShell there:

```
cd ([Environment]::GetFolderPath('Desktop'))
Expand-Archive .\androidhead-win-x64.zip -DestinationPath .\androidhead -Force
cd .\androidhead
Get-ChildItem -Recurse | Unblock-File
.\p2pm-androidhead-spike.exe selftest
.\p2pm-androidhead-spike.exe camera --list --formats
.\p2pm-androidhead-spike.exe camera --seconds 30
```

- `selftest` must end with `49 passed, 0 failed`.
- `camera --list --formats` lists the cameras and their formats, one line per camera such as `[0] <name> (DirectShow): ...`. If `[0]` is not the webcam (a phantom `Default (VideoForWindows)` entry can come first), add `--device 1`, or the right number, to the last command.
- Before the last command, show a QR code to the webcam: open `spikes\AndroidHead\run\qr-screen.png` on laptop 1 (or print it) at full brightness and hold laptop 2's webcam about 30 cm from it. The command then prints `camera: QR code decoded from frame <n> in <ms> ms: 20 characters` and, when it stops, a summary line with at least `1 decoded`. The 20 characters are the code `p2pm-scan-check-0001`; the command logs the length of the text, not the text.
- `no frame arrived`: check Windows Settings, Privacy & security, Camera, "Let desktop apps access your camera", and close other programs that use the camera. `could not be opened`: try the next `--device`. Frames but `0 decoded` and a count of `JPEG frame(s)`: the webcam offers only compressed frames at that size, which is a finding for S17 because the spike cannot decode JPEG; send the log.
- The logs are in `run\` under the folder where you ran the commands (`selftest-<time>.txt`, `camera-<time>.txt`). Copy the `camera-*.txt` files to `spikes\AndroidHead\run\laptop2\` on laptop 1, or paste their text.

## 1. The PC tool

`p2pm-androidhead-spike <command>`; the logs go to `spikes\AndroidHead\run` when the program sits inside this spike's folder (as with `dotnet run --project`), and to `run` in the current folder otherwise (as on laptop 2).

| Command | What it does |
| --- | --- |
| `selftest` | QR round trips for three payloads (short, 241 characters, Cyrillic text) in four rotations, as light-on-dark, with row padding as in a camera plane, on a larger canvas; blank and noise frames; the camera-frame parser, including a bitmap that claims a huge width; the PNG writer; the probe listener and the probe command over loopback. 49 checks, no camera, no network |
| `probe <host>` | Sends `PING` lines to the phone's listener and prints the connect time, the reply time and the phone's state. `--port <n>` (45821), `--delay <s>` before the first probe (0), `--every <s>` (60), `--count <n>` (1), `--timeout <s>` after which a reply counts as late (60) |
| `qr <text>` | Prints a QR code as text blocks, or writes a PNG with `--png <file> [--scale <n>] [--canvas <width>x<height>]` (the code centred on a larger white image, for the emulator's virtual scene); `--dark` for a dark terminal |
| `camera` | Lists the computer's cameras with FlashCap (`--list`, `--formats`) and decodes a QR code from the live picture (`--device <n>`, `--width <n>` preferred frame width, `--seconds <n>`, `--any` to keep going after the first code). An open failure is reported as "no camera" |
| `apk-libs <apk>` | Reads every `.so` under `lib/` in an APK, prints the alignment of its load segments and fails any 64-bit library below 16 KB |

The probe protocol is one line each way, `PING <sequence> <sent ms>` and `PONG <sequence> <sent ms> <received ms> <state>`; the listener reads at most 256 bytes and closes on a line that is longer or that it does not know.

## 2. The Android app

The buttons are the ones in Part B. The list next to **Wake lock** chooses the service type, and the box holds the listener's port. The app is `net10.0-android36.1` with a minimum API of 26 and the application id `io.github.kaunazdagaz.p2pmessenger.androidheadspike`. It declares `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `FOREGROUND_SERVICE_SPECIAL_USE`, `POST_NOTIFICATIONS`, `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`, `WAKE_LOCK` and `CAMERA`; the camera is not required. The tor binary is `libTor.so` from the Expert Bundle, shipped as a native library and extracted at install (`android:extractNativeLibs="true"`).

**Control from a PC with `adb`.** The activity takes the extra `run`, so every button can be pressed from the PC. The phone must be unlocked and the app in front when a service is started (an app behind the lock screen cannot start a foreground service: Android refuses with `ForegroundServiceStartNotAllowedException`); the activity itself is named explicitly because the launcher entry cannot be resolved by an implicit `am start`:

```
$pkg = "io.github.kaunazdagaz.p2pmessenger.androidheadspike"
& $adb shell am start -n $pkg/io.github.kaunazdagaz.androidheadspike.MainActivity --es run info
& $adb shell am start -n $pkg/io.github.kaunazdagaz.androidheadspike.MainActivity --es run fgs-start --es kind SpecialUse --ez wakelock true --ei port 45822
```

`run` is one of `info`, `fgs-start`, `fgs-stop`, `notifications`, `camera-permission`, `battery`, `scan-activity`, `scan-view`, `scan-view-hide`, `tor-selftest`, `tor-pair`, `tor-serve`, `tor-stop`; `kind` is `ConnectedDevice` (the default), `SpecialUse` or `Untyped`; `wakelock` and `port` are as in the app. Read the answer with `& $adb logcat -d -s AndroidHeadSpike`.

**Logs on the phone.** The service writes `service-<time>.txt` (everything the app logged since it was opened, plus the heartbeats) and each tor command `console-<command>.txt` into the app's private `files/run` folder. The app is a Debug build, so `adb` can fetch them with `run-as`:

```
& $adb shell "run-as $pkg sh -c 'cd files/run && tar -cf - .' > /data/local/tmp/androidhead.tar"
& $adb pull /data/local/tmp/androidhead.tar spikes\AndroidHead\run\phone.tar
& $adb shell rm /data/local/tmp/androidhead.tar
mkdir spikes\AndroidHead\run\phone -Force
tar -xf spikes\AndroidHead\run\phone.tar -C spikes\AndroidHead\run\phone
```

**The manifest without the service prerequisites.** `Properties\AndroidManifest.Negative.xml` leaves out `CHANGE_WIFI_MULTICAST_STATE` (the prerequisite of `connectedDevice`) and `FOREGROUND_SERVICE_SPECIAL_USE`. Build with `-p:AndroidManifest=Properties\AndroidManifest.Negative.xml` and **Start service** on Android 14 or later fails with a `SecurityException` that names what is missing; the app survives and the log says so. This is the check that a declaration error is caught in the first dry run, not in the field.

**The 16 KB check.** `p2pm-androidhead-spike apk-libs <apk>` reads every `.so` under `lib/` in an APK, prints the alignment of its load segments and fails any 64-bit library below 16 KB. A 16 KB emulator image runs the app for real; **Info** then shows `page size 16384 bytes`.

## 3. Emulators

The agent used three Android emulators on laptop 1: `p2pm-api26` (Android 8.0), `p2pm-api31` (Android 12) and `p2pm-api36-16k` (Android 16, 16 KB pages). The Android 17 16 KB system image crashed the emulator's graphics (`surfaceflinger`) on this PC, so Android 17 was checked on the phone only. Useful commands: `adb emu redir add tcp:45822:45821` makes the emulated phone's listener reachable on the PC's port 45822 through the emulator's NAT, which is the realistic path (`adb forward` goes through `adbd`); `adb shell dumpsys battery unplug` and `adb shell dumpsys deviceidle enable all` then `force-idle` put it into deep Doze (and `dumpsys battery reset`, `deviceidle unforce` undo it); the emulator's `-camera-back imagefile:<png>` makes the emulated back camera show a picture, for example the output of `qr --png`. That camera does not centre the picture: in this spike it showed the picture turned 90° counter-clockwise, at 1.5 image pixels per frame pixel, centred on image x = 362 and y = 480, so a 1280 by 960 image needed the code drawn around that point; one code only, because several identical codes in a lattice make ZXing pick a wrong triple of finder patterns. `-camera-back virtualscene -virtualscene-poster wall=<png>` does not show the poster from the default viewpoint. The emulator's processor never sleeps, so the heartbeat's "processor slept" stays at zero there.

## 4. What the spike writes to disk

On laptop 1, in `spikes\AndroidHead\run`: the logs of the PC tool (`selftest-`, `probe-`, `camera-<time>.txt`), the APKs and screenshots of the emulator runs, and whatever you fetch from the phone. On the phone, in the app's private `files/run`: `service-<time>.txt` and `console-<command>.txt`; the tor commands also keep their state there (tor's data folder, onion keys and `results.csv`); the scanner saves the first and the 90th frame it analysed as `scan-frame-1.png` and `scan-frame-90.png` (luminance only), so that a scan that finds nothing can be examined. Uninstalling the app removes them.
