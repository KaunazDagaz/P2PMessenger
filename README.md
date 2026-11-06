# P2PMessenger

A friend-to-friend messenger for small groups of people, for the times their usual messenger stops working: weak or no internet, travel without mobile data, or an internet shutdown. Messages are end-to-end encrypted and travel directly between the devices, over a shared Wi-Fi network or one phone's hotspot, or over Tor onion services. There is no server. Clients for Windows, Linux and Android.

Bachelor diploma project "Development of a Secure Decentralized P2P Messenger with End-to-End Encryption" by Mikita Besau, European Humanities University.

**Status:** the repository is scaffolded and the app opens an empty window. No feature is built yet.

## Repository layout

| Folder | Contents |
| --- | --- |
| `src/P2PMessenger.Core` | Identity, keys, contact codes, crypto wrappers, Noise sessions, message formats. No I/O and no platform API. |
| `src/P2PMessenger.Transport` | TCP, LAN discovery, tor control and SOCKS5, connections and reachability |
| `src/P2PMessenger.Storage` | SQLite schema and migrations, repositories, per-record encryption |
| `src/P2PMessenger.Application` | Use cases, and the interfaces for platform services that the heads implement |
| `src/P2PMessenger.UI` | Avalonia views and view models shared by both heads |
| `src/P2PMessenger.Desktop` | Windows and Linux head |
| `src/P2PMessenger.Android` | Android head |
| `tests/` | One test project per layer, integration tests and headless UI tests |

Dependency rules: `Core` references no other project and uses no I/O or platform API; `Transport` and `Storage` reference `Core`; `Application` references `Core`, `Transport` and `Storage`; `UI` references `Application`; the two heads reference `UI`. The architecture tests in `P2PMessenger.Integration.Tests` and `P2PMessenger.Core.Tests` enforce them.

## Prerequisites

- .NET SDK 10.0.403, or a later 10.0.4xx patch (`global.json`).
- For the Android head only: the android workload (`dotnet workload install android`), the Android SDK with the API 36.1 platform, and a JDK (Microsoft OpenJDK 21 is recommended). On a machine without them, `dotnet build src/P2PMessenger.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\Android\sdk -p:JavaSdkDirectory=C:\Android\jdk -p:AcceptAndroidSDKLicenses=true` installs both into the given folders (absolute paths without spaces). Then set the `ANDROID_HOME` and `JAVA_HOME` environment variables to those folders so later builds find them.

`P2PMessenger.Desktop.slnf` holds every project except the Android head, so desktop work does not need the android workload.

## Build, test and run

```
dotnet restore P2PMessenger.Desktop.slnf
dotnet build P2PMessenger.Desktop.slnf
dotnet test --solution P2PMessenger.Desktop.slnf
dotnet format P2PMessenger.Desktop.slnf --verify-no-changes
dotnet run --project src/P2PMessenger.Desktop
dotnet build src/P2PMessenger.Android -c Debug
dotnet list P2PMessenger.Desktop.slnf package --vulnerable --include-transitive
```

- Warnings are errors, and code style, unnecessary usings included, is checked on build (`Directory.Build.props`, `.editorconfig`). Missing XML documentation is not reported. A failed download of vulnerability data (NU1900) stays a warning, so building offline works.
- The tests run on Microsoft Testing Platform (`global.json`). Every `dotnet test` run also writes a Cobertura coverage report into the `TestResults` folder next to each test assembly (`tests/Directory.Build.props`).
- Coverage instrumentation rewrites `P2PMessenger.Core.dll` next to the test assembly while the tests run, so the Core architecture test reads the copy that `P2PMessenger.Core.Tests.csproj` puts in the `architecture` subfolder on build.
- A test project that runs no tests ends with exit code 8, and then `dotnet test` fails. `P2PMessenger.Transport.Tests`, `P2PMessenger.Storage.Tests` and `P2PMessenger.Application.Tests` have no tests yet, so their project files pass `--ignore-exit-code 8`. Remove it from a project when it gets its first test.
- Every restore writes a `packages.lock.json` file next to each project. Commit them. After changing a version in `Directory.Packages.props`, run `dotnet restore --force-evaluate` to update them.
- Avalonia's build tooling sends anonymous build-time telemetry to Avalonia, and the .NET CLI sends its own; neither affects the app. Set `AVALONIA_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` to turn them off.

## Licence

Not chosen yet.
