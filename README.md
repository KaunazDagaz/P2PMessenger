# P2PMessenger

A friend-to-friend messenger for small groups of people, for the times their usual messenger stops working: weak or no internet, travel without mobile data, or an internet shutdown. Messages are end-to-end encrypted and travel directly between the devices, over a shared Wi-Fi network or one phone's hotspot, or over Tor onion services. There is no server. Clients for Windows, Linux and Android.

Bachelor diploma project "Secure Decentralized P2P Messenger with End-to-End Encryption" by Mikita Besau, European Humanities University.

**Status:** a first working version. Two apps on one network create identities, add each other by contact codes passed as text or files, compare fingerprints and chat, end-to-end encrypted; apps whose codes were not added are refused. On Windows and Linux the identity, the contacts and the chats are kept in an encrypted local database and survive closing the app (see [Database](#database)); the Android app still keeps them in memory. A message to a contact who is not connected waits on the device, also across restarts on Windows and Linux, and is sent when the contact connects; it shows "Waiting" until the contact's app confirms that it has it, then "Received by the contact's app". In Debug builds the contact's address is typed in by hand (see [Trying the app](#trying-the-app)). Underneath: `P2PMessenger.Core` holds the key set, the contact codes and the fingerprints, and the encrypted sessions between two contacts (a Noise KK handshake), their frames and the message types; `P2PMessenger.Transport` holds the TCP connections, the refusal of callers that are not contacts, limits against flooding, one session per contact, reconnects and keep-alives; `P2PMessenger.Storage` holds the database, its migrations and the encryption of stored data; `P2PMessenger.Application` holds the identity, the contacts, the chats, the outbox that sends and resends waiting messages, the delivery states, the connections to contacts and the opening of the stored data; `P2PMessenger.UI` holds the screens; the Windows and Linux head protects the database's key with the platform's key store.

## Repository layout

| Folder | Contents |
| --- | --- |
| `src/P2PMessenger.Core` | Identity, keys, contact codes, crypto wrappers, Noise sessions, message formats. No I/O and no platform API. |
| `src/P2PMessenger.Transport` | TCP, LAN discovery, tor control and SOCKS5, connections and reachability |
| `src/P2PMessenger.Storage` | SQLite schema and migrations, repositories, per-record encryption |
| `src/P2PMessenger.Application` | Use cases, and the interfaces for platform services that the heads implement |
| `src/P2PMessenger.UI` | Avalonia views and view models shared by both heads |
| `src/P2PMessenger.Desktop` | Windows and Linux head, with the platform key stores (DPAPI on Windows, the Secret Service on Linux) |
| `src/P2PMessenger.Android` | Android head |
| `tests/` | One test project per layer and one for the desktop head, integration tests and headless UI tests |

Dependency rules: `Core` references no other project and uses no I/O or platform API; `Transport` and `Storage` reference `Core`; `Application` references `Core`, `Transport` and `Storage`; `UI` references `Application`; the two heads reference `UI`. The architecture tests in `P2PMessenger.Integration.Tests` and `P2PMessenger.Core.Tests` enforce them.

## Prerequisites

- .NET SDK 10.0.404 (`global.json`). A later 10.0.4xx patch also builds, but its restore changes the Android head's lock file (see Continuous integration).
- For the Android head only: the android workload (`dotnet workload install android`), the Android SDK with the API 36.1 platform, and a JDK (Microsoft OpenJDK 21 is recommended). On a machine without them, `dotnet build src/P2PMessenger.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\Android\sdk -p:JavaSdkDirectory=C:\Android\jdk -p:AcceptAndroidSDKLicenses=true` installs both into the given folders (absolute paths without spaces). Then set the `ANDROID_HOME` and `JAVA_HOME` environment variables to those folders so later builds find them.
- On Windows, NSec's installation notes say that the native libsodium it loads needs the Microsoft Visual C++ Redistributable (x64). A developer machine usually has it and a clean Windows installation may not, so the tests of `P2PMessenger.Core.Tests` may fail to load the library there.

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
- A test project that runs no tests ends with exit code 8, and then `dotnet test` fails; a new test project without tests needs `--ignore-exit-code 8` in its project file until its first test.
- The integration tests and the UI tests open TCP connections on the loopback interface. The storage, application and integration tests create databases in the temporary folder and delete them afterwards.
- `P2PMessenger.Desktop.Tests` tests the key stores of the desktop head. Its DPAPI tests run only on Windows. Its Secret Service tests run only on Linux with an unlocked Secret Service on the D-Bus session bus, and are reported as skipped elsewhere (as on a CI runner, which has no session bus).
- A Debug APK from `dotnet build` holds only the Android part of the app and runs only when deployed with `dotnet build src/P2PMessenger.Android -t:Run`. To install it by hand, build it with `-p:EmbedAssembliesIntoApk=true`.
- Every restore writes a `packages.lock.json` file next to each project. Commit them. After changing a version in `Directory.Packages.props`, run `dotnet restore --force-evaluate` to update them.
- Every text the app shows is in `src/P2PMessenger.UI/Resources/UiText.resx`, with its class `UiText.Designer.cs` checked in next to it. Visual Studio and Rider regenerate the class when the `.resx` is saved; elsewhere, after editing the `.resx`, run `dotnet msbuild src/P2PMessenger.UI -t:GenerateResourceDesigners` (a target in `Directory.Build.targets`). A test fails when the class and the `.resx` differ.
- Avalonia's build tooling sends anonymous build-time telemetry to Avalonia, and the .NET CLI sends its own; neither affects the app. Set `AVALONIA_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` to turn them off.

## Trying the app

Until the apps find each other on the network, a Debug build connects to addresses typed in by hand. To try two apps:

1. Start two Debug builds, on two devices on one network or both on one PC (`dotnet run --project src/P2PMessenger.Desktop`, or the Android head deployed with `dotnet build src/P2PMessenger.Android -t:Run`). Two apps on one PC need their own data folders: start the second with `dotnet run --project src/P2PMessenger.Desktop -- --data <folder>` (see [Database](#database)).
2. In each, create an identity. Each app listens on TCP port 45821. A second app on the same PC finds the port taken: open "Network (debug)", enter another port, such as 45822, and choose "Listen on this port". The page also shows the device's own addresses.
3. In each, open "My code", copy the code or save it to a file, and pass it to the other app.
4. In each, open "Add contact", paste the other's code or open their file, and choose "Check code" and "Add contact". In at least one of the two, enter the other's address in the form `192.168.1.20:45821` (or `127.0.0.1:45822` on one PC), here or later on the "Network (debug)" page.
5. When the first connection succeeds, both apps say that the contact is added and that the app checked the key, not the person. Compare the fingerprints under "Fingerprints", then send messages. Each of your messages shows "Waiting" until the other app confirms it and "Received by the contact's app" after that; select the state to see in one line what it confirms. Close one app, send from the other, and start the first again: the waiting messages go out once the two are connected.

On Windows, the firewall asks on the first run whether the app may accept connections; allow it for the network you use. Release builds listen on the same port but have no way to enter an address yet. If a first connection fails, the "Network (debug)" page and the contact's conversation list the possible reasons.

## Database

Each device keeps its data in one local SQLite database; there is no server. The database materials:

| What | Where |
| --- | --- |
| Data dictionary (tables, columns, types, keys, constraints), logical schema with an ER diagram, how stored data is protected, normalisation | [`src/P2PMessenger.Storage/README.md`](src/P2PMessenger.Storage/README.md) |
| DDL: the migration scripts that create and change the schema | [`src/P2PMessenger.Storage/Migrations`](src/P2PMessenger.Storage/Migrations) |
| Migration runner and connection settings | `MigrationCatalog`, `MigrationRunner` and `SqliteDatabase` in [`src/P2PMessenger.Storage`](src/P2PMessenger.Storage) |
| Tests that create a database from the scripts and check it | [`tests/P2PMessenger.Storage.Tests`](tests/P2PMessenger.Storage.Tests) |

The schema is created only by the scripts, never by hand. To create a database from them and check it:

```
dotnet test --project tests/P2PMessenger.Storage.Tests
```

or, with the SQLite command-line shell (3.37 or later):

```
sqlite3 :memory: ".read src/P2PMessenger.Storage/Migrations/0001_initial.sql" ".read src/P2PMessenger.Storage/Migrations/0002_message_id.sql" ".read src/P2PMessenger.Storage/Migrations/0003_outgoing_delivery.sql" ".tables"
```

Give a file name instead of `:memory:` to keep the database, for example to open it in DB Browser for SQLite; delete the file afterwards, it is not part of the repository.

### Where the app keeps its data

The Windows and Linux app keeps its database, `messenger.db`, in `%LOCALAPPDATA%\P2PMessenger` on Windows and in `~/.local/share/P2PMessenger` on Linux (`$XDG_DATA_HOME/P2PMessenger` when that is set). `--data <folder>` (or `--data=<folder>`) on the command line uses another folder. Every name, key, text and time in it is encrypted with a random data key, and the data key is wrapped by the platform's key store: DPAPI for the current Windows user, or a Secret Service such as GNOME Keyring or KWallet on Linux. A copy of the folder cannot be read on another computer or under another user account.

On a Linux desktop without a Secret Service, the app writes no database: it keeps the session's data in memory and says that nothing is saved. A passphrase will protect the data there in a later version. The Android app also keeps its data in memory for now. If the app cannot open its data (the key store is locked or unreachable, the folder was copied from elsewhere, the data was changed or comes from a newer version), it shows why and offers to try again; it never replaces data it cannot read.

## Continuous integration

GitHub Actions runs `.github/workflows/ci.yml` on every push and pull request. The actions are pinned to commit SHAs, and the workflow can only read the repository.

- **Desktop**, on Windows and Linux: a locked restore, the format check, a Release build (warnings are errors) and the tests with coverage. The coverage reports are uploaded as the `test-results-<OS>` artifact. On Linux the job also fails if `dotnet list package --vulnerable --include-transitive` finds a vulnerable package.
- **Android**, on Linux: Microsoft OpenJDK 21 (the newest JDK the android workload supports), the android workload and the Android SDK parts the project needs, then a locked restore, a Debug build with the .NET code inside the APK, and the same check for vulnerable packages.
- For pull requests and `main`, the desktop builds and the APK are uploaded as artifacts. Each run signs the APK with a new debug key, so uninstall the previous build before installing another one.

Dependabot (`.github/dependabot.yml`) proposes updates once a week for the NuGet packages (all of them in one pull request), the .NET SDK in `global.json` (within .NET 10) and the actions. It skips xunit.v3.mtp-v2 4.x and Xamarin.AndroidX.Core.SplashScreen 1.2.0.3, which do not work with Avalonia 12.1.5 yet.

The Android head's lock file records `Microsoft.NET.ILLink.Tasks`, whose version comes with the .NET SDK, so CI has to use the SDK that wrote the lock file: `global.json` pins it with `rollForward: patch`, and CI installs exactly that version. To move to a newer SDK, change `global.json`, run `dotnet restore P2PMessenger.slnx --force-evaluate` with that SDK, and commit both. A Dependabot pull request that updates the SDK fails the Android job until its branch gets the updated lock file.

## Licence

Not chosen yet.
