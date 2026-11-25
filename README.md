# P2PMessenger

[![CI](https://github.com/KaunazDagaz/P2PMessenger/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/KaunazDagaz/P2PMessenger/actions/workflows/ci.yml)

A friend-to-friend messenger for small groups of people, for the times their usual messenger stops working: weak or no internet, travel without mobile data, or an internet shutdown. Messages are end-to-end encrypted and travel directly between the devices, over a shared Wi-Fi network or one phone's hotspot, or over Tor onion services. There is no server. Clients for Windows, Linux and Android.

Bachelor diploma project "Secure Decentralized P2P Messenger with End-to-End Encryption" by Mikita Besau, European Humanities University.

**Status:** a first working version. Two apps on one network create identities, add each other by contact codes passed as text or files, compare fingerprints and chat, end-to-end encrypted; apps whose codes were not added are refused. The identity, the contacts and the chats are kept in an encrypted local database and survive closing the app, on Windows, Linux and Android (see [Database](#database)). An optional passphrase protects the stored data: the app asks for it at start, sends and receives nothing until it is entered, and locks after 5 minutes in the background or when "Lock" is pressed, while messages keep arriving. A message to a contact who is not connected waits on the device, also across restarts, and is sent when the contact connects; it shows "Waiting" until the contact's app confirms that it has it, then "Received by the contact's app". A chat, a message that is still waiting, or all data can be deleted after a confirmation; deletion happens only on this device, and the contact's app keeps its copy. Two apps on the same Wi-Fi network or one phone's hotspot find each other without anyone typing an address, and each contact is shown as "Reachable nearby" or "Not reachable now"; with no Wi-Fi and no internet, the app says that one person can share a hotspot (see [Finding contacts on the local network](#finding-contacts-on-the-local-network)). When the build includes tor and Tor is not turned off, the app starts it, publishes its own onion service and says on the contact list whether the internet route over Tor works and, if not, why; contacts on different networks reach each other through their onion services, a contact reachable both ways is reached over Tor, the local network carries the messages whenever Tor does not, and each contact is shown as "Reachable over Tor", "Reachable nearby" or "Not reachable now" (see [Tor](#tor)). Underneath: `P2PMessenger.Core` holds the key set, the contact codes and the fingerprints, the format of the private announcements, and the encrypted sessions between two contacts (a Noise KK handshake), their frames and the message types; `P2PMessenger.Transport` sends and screens the announcements that find contacts on the local network, starts and controls tor, connects to contacts' onion services through tor's SOCKS port, and holds the TCP connections, the refusal of callers that are not contacts, limits against flooding, one session per contact, reconnects and keep-alives; `P2PMessenger.Storage` holds the database, its migrations and the encryption of stored data; `P2PMessenger.Application` holds the identity, the contacts, the chats, the outbox that sends and resends waiting messages, the delivery states, the connections to contacts, the opening of the stored data, the passphrase and the lock; `P2PMessenger.UI` holds the screens; the Windows, Linux and Android heads protect the database's key with the platform's key store.

## Repository layout

| Folder | Contents |
| --- | --- |
| `src/P2PMessenger.Core` | Identity, keys, contact codes, crypto wrappers, Noise sessions, message formats. No I/O and no platform API. |
| `src/P2PMessenger.Transport` | TCP, LAN discovery, tor control and SOCKS5, connections and reachability |
| `src/P2PMessenger.Storage` | SQLite schema and migrations, repositories, per-record encryption |
| `src/P2PMessenger.Application` | Use cases, and the interfaces for platform services that the heads implement |
| `src/P2PMessenger.UI` | Avalonia views and view models shared by both heads; the front-end documentation is in [`src/P2PMessenger.UI/README.md`](src/P2PMessenger.UI/README.md) |
| `src/P2PMessenger.Desktop` | Windows and Linux head, with the platform key stores (DPAPI on Windows, the Secret Service on Linux) |
| `src/P2PMessenger.Android` | Android head, with the Android Keystore as its key store |
| `tests/` | One test project per layer and one for each head, integration tests, headless UI tests and end-to-end tests |
| `tools/` | `get-tor.ps1`, which downloads the pinned Tor Expert Bundles into `tools/tor` (not committed; see [Tor](#tor)) |
| `API.md` | The description of the API: the wire protocol between two apps and the application interface used by the screens (see [Back-end](#back-end)) |

Dependency rules: `Core` references no other project and uses no I/O or platform API; `Transport` and `Storage` reference `Core`; `Application` references `Core`, `Transport` and `Storage`; `UI` references `Application`; the two heads reference `UI`. The architecture tests in `P2PMessenger.Integration.Tests` and `P2PMessenger.Core.Tests` enforce them.

## Front-end

The client is an Avalonia application written in C# with the MVVM pattern. One code base, `P2PMessenger.UI`, runs on Windows, Linux and Android; the two heads start it and provide the platform services. The front-end works with the application layer of its own process. Its documentation is in [`src/P2PMessenger.UI/README.md`](src/P2PMessenger.UI/README.md):

| What | Where |
| --- | --- |
| How to build, run and test the client | [Build, run and test](src/P2PMessenger.UI/README.md#build-run-and-test) |
| Where each requirement of the front-end criterion is implemented, and its status | [Where each requirement is implemented](src/P2PMessenger.UI/README.md#where-each-requirement-is-implemented) |
| Technology stack and why | [Technology stack and why](src/P2PMessenger.UI/README.md#technology-stack-and-why) |
| Architecture diagram, project structure, MVVM and state management | [Architecture](src/P2PMessenger.UI/README.md#architecture) |
| Navigation diagram and user flows | [Navigation and user flows](src/P2PMessenger.UI/README.md#navigation-and-user-flows) |
| Forms and validation, error handling, loading states, logging | [Forms and validation](src/P2PMessenger.UI/README.md#forms-and-validation), [Error handling](src/P2PMessenger.UI/README.md#error-handling), [Loading states](src/P2PMessenger.UI/README.md#loading-states), [Logging](src/P2PMessenger.UI/README.md#logging) |
| The API the front-end works with | [The API the front-end works with](src/P2PMessenger.UI/README.md#the-api-the-front-end-works-with) |
| Unit, UI and end-to-end tests | [Tests](src/P2PMessenger.UI/README.md#tests) |
| What is not built yet | [Planned](src/P2PMessenger.UI/README.md#planned) |

To try the client, run `dotnet run --project src/P2PMessenger.Desktop` and follow [Trying the app](#trying-the-app).

## Back-end

The app has no server, so it has no HTTP service. The back-end is four .NET 10 libraries that run inside every app, `P2PMessenger.Core`, `P2PMessenger.Transport`, `P2PMessenger.Storage` and `P2PMessenger.Application`; the screens call them in the same process. The criterion's exception for software that needs no server framework applies, and the four things it asks to implement directly (logging, error handling, serialization and data binding, authorization and authentication) are rows of the table below. The interfaces of the back-end are described in [`API.md`](API.md): the wire protocol between two apps and the application interface that the screens use. The database is described in [Database](#database).

| Requirement | Here | Status |
| --- | --- | --- |
| A modern framework | .NET 10 and C# 14, with Microsoft.Extensions.Logging. Avalonia is used for the screens only. There is no server framework because there is no server | Done (the exception above) |
| A database | SQLite with 13 `STRICT` tables in 3NF, primary and foreign keys, `CHECK` constraints and numbered migrations. See [Database](#database) and [`src/P2PMessenger.Storage/README.md`](src/P2PMessenger.Storage/README.md) | Done |
| An ORM | Not used. `src/P2PMessenger.Storage/Repositories` use Microsoft.Data.Sqlite with parameterised SQL, because every name, key, text and time is encrypted per record and lookups go through keyed MAC columns. See [Choices that differ from the criterion](#choices-that-differ-from-the-criterion) | Not used |
| Layered architecture | Presentation: `P2PMessenger.UI` and the two heads. Business logic: `P2PMessenger.Application`, `P2PMessenger.Core` and `P2PMessenger.Transport`. Data access: `P2PMessenger.Storage`. The dependency rules of [Repository layout](#repository-layout) are enforced by the architecture tests in `tests/P2PMessenger.Integration.Tests/Architecture` and `tests/P2PMessenger.Core.Tests/Architecture` | Done |
| SOLID | Each class has one job. Everything that differs between platforms or routes sits behind an interface and is injected through constructors: for example `IKeyProtector` (DPAPI, Secret Service, Android Keystore) and `IDialer` (`TcpDialer`, `OnionDialer`), listed in [`API.md`](API.md#94-what-each-platform-implements). Layers depend only on the layers below them, which the architecture tests check | Done |
| A described API | [`API.md`](API.md): the wire protocol with byte layouts and reference vectors, and the application interface with its result values. There is no HTTP API, so no OpenAPI | Done |
| Global error handling | `GlobalFailureHandler` in `src/P2PMessenger.Application/Errors`, a back-end handler that does not depend on the UI. It hooks the failed tasks that nobody observed and the exceptions that end the process (`Install`), and the connections, the discovery and the delivery report to it (`IFailureSink`, given as `MessengerSetup.Failures`) the unexpected failures that they catch in their background work, instead of each logging its own. For each exception it logs the type once (an exception reported again is ignored, out-of-memory is left to end the process), counts it and raises `FailureReported`. The front-end only adds its own source, the UI thread's dispatcher, and turns the event into a notice (`GlobalErrorHandler` in `src/P2PMessenger.UI/Services`). Around it: expected failures are returned as typed results (`SendResult`, `CodeCheck`, `AddContactResult`, `LocalDataResult`), one predicate, `Failures` in `src/P2PMessenger.Core`, decides which exceptions are failures in place of the same filter in every `catch`, and an error that a command catches shows its message and is logged (page, operation and exception type) by `FailureLog`. Tests: `GlobalFailureHandlerTests`, `FailureReportingTests`, `FailuresTests`, `GlobalErrorHandlerTests`, `CommandFailureLogTests`. See [Error handling](src/P2PMessenger.UI/README.md#error-handling) and [`API.md`](API.md#92-services) | Done |
| Structured logging | `ILogger` with source-generated messages that have event IDs and named parameters (`src/P2PMessenger.Transport/Logging/TransportLog.cs`, 29 messages; the outbox; `GlobalFailureHandler`; `FailureLog`). The app writes them as JSON lines to a rolling log file, see [Logs](#logs). Logs never hold keys, names, texts or addresses: `LoggingTests` and `LogFileTests` check this | Done |
| Authentication and authorization | Mutual authentication with a Noise KK handshake (`src/P2PMessenger.Core/Noise`, `Sessions`). A caller that is not a contact is refused before anything reaches the user, and limits stop flooding (`src/P2PMessenger.Transport/Admission`, `Connections/KnownContacts.cs`) | Done |
| Serialization and data binding | Binary formats with strict parsers that return a refusal and never throw (`src/P2PMessenger.Core/Contacts`, `Messages`, `Sessions`, `Discovery`), described in [`API.md`](API.md); compiled Avalonia bindings in the UI | Done |
| Input validation | Contact codes, names, texts and passphrases pass strict rules (`ContactCode`, `DisplayName`, `TextMessage`, `PassphrasePolicy`); every single-bit change of a contact code is refused (tested); every SQL statement is parameterised | Done |
| Hashing and secrets | BLAKE2b, HKDF-SHA-256, SHA3-256 and Argon2id, all through libsodium (NSec); no MD5 or SHA-1. Private keys are made on the device and stored encrypted under a key that the platform's key store wraps. The repository holds no keys, passwords or signing keystores (`.gitignore`) | Done |
| Configuration | Ports, timeouts and limits are named option classes (`ConnectionOptions`, `DiscoveryOptions`, `TorOptions`, `DeliveryOptions`, `AdmissionLimits`) with defaults in code. A client app has no deployment environment to read them from; the user's own settings are kept in the app's settings file or preferences | Done |
| Dependencies | No paid libraries. `dotnet list package --vulnerable --include-transitive` finds nothing and fails CI when it does; Dependabot proposes updates every week. See [Continuous integration](#continuous-integration) | Done |
| Version control | Git, with the history of every step from the first commit, merged through pull requests | Done |
| Production deployment | A GitHub Release with a Windows build, a Linux build and an APK. Until then, the CI run on `main` keeps the desktop builds and the Debug APK as artifacts | Planned, see [Planned](#planned) |
| Unit tests with at least 70 % coverage | See [Tests and coverage](#tests-and-coverage) | Done |

### Logs

The app writes its log to the `logs` folder inside its data folder: `%LOCALAPPDATA%\P2PMessenger\logs` on Windows, `~/.local/share/P2PMessenger/logs` on Linux (the folder given with `--data` replaces the data folder on both), and `no_backup/P2PMessenger/logs` in the app's folder on Android, which Android does not back up. The current file is `p2pmessenger.log`. At 1 MiB it becomes `p2pmessenger.1.log`, then `p2pmessenger.2.log`, and the oldest is deleted, so the log never takes more than 3 MiB. Each line is one JSON object, for example:

```
{"time":"2026-11-25T12:30:05.123Z","level":"Error","category":"P2PMessenger.Application.Errors.GlobalFailureHandler","eventId":1,"event":"UnexpectedFailure","template":"Unexpected failure of type {ExceptionType} in {Source}","message":"Unexpected failure of type System.IO.IOException in Connections","fields":{"ExceptionType":"System.IO.IOException","Source":"Connections"}}
```

`template` and `fields` hold the message as a template and its named parameters, which is what a tool filters on, and `message` is the same text for a person. An exception object, when a log call passes one, adds `"exception"` with its type name. To read the log:

```
Get-Content "$env:LOCALAPPDATA\P2PMessenger\logs\p2pmessenger.log" | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object level -eq Error
jq 'select(.level == "Error")' ~/.local/share/P2PMessenger/logs/p2pmessenger.log
adb exec-out run-as io.github.kaunazdagaz.p2pmessenger cat no_backup/P2PMessenger/logs/p2pmessenger.log
```

- **What is logged.** Information and above, in Debug and Release builds: the listening ports, the start and state of Tor and its onion service, network changes, warnings (a port that cannot be opened, tor that could not start or stopped), unexpected failures, unhandled errors and the errors that commands catch. What happens between contacts (connections opening and closing, refusals of callers) is logged only at the Debug level, which the file does not take. The .NET trace output, which a debugger or logcat shows, still gets Information in Debug builds and only errors in Release builds.
- **What never gets in.** Only plain values are written: numbers, booleans, names of enumeration values and strings of at most 256 characters; any other value is written as its type name, and an exception only as its type name, never its message or its stack. The formatter of a log call is never called, so a name, key, address or text cannot get in through a message. `LogFileTests` runs two apps that chat and checks the whole file for the names, texts, keys and addresses involved.
- **Failures.** A log call never throws. When the file cannot be written (full disk, no permission) the line is dropped and writing is tried again after 30 seconds.
- **Delete all data** does not delete the log, which holds no names, keys, texts or addresses.

### Tests and coverage

Each test project writes its own Cobertura report, and the integration and UI tests also run the lower layers, so the coverage of the back-end is the merge of all reports. To measure it, from the repository root (delete `TestResults` first, because reports of earlier runs stay there and would be merged too):

```
dotnet test --solution P2PMessenger.Desktop.slnf --configuration Release --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/*.xml" "-targetdir:TestResults/coverage" "-reporttypes:TextSummary;Html" "-assemblyfilters:+P2PMessenger.Core;+P2PMessenger.Transport;+P2PMessenger.Storage;+P2PMessenger.Application;-*.Tests"
```

`TestResults/coverage/Summary.txt` gives the totals and `TestResults/coverage/index.html` the report by class and file. On 2026-11-25, on Windows, 1,799 tests ran and none failed; 20 were skipped, because they need Linux, a Secret Service or a real tor. The merged line coverage of the four assemblies that hold the business logic was 93.0 % (branch coverage 86.7 %): `P2PMessenger.Core` 95.2 %, `P2PMessenger.Storage` 94.8 %, `P2PMessenger.Application` 94.2 % and `P2PMessenger.Transport` 90.2 %. The platform code of the two heads is checked on the platforms themselves (see [Build, test and run](#build-test-and-run)). CI makes the same merge for every assembly of the desktop solution on every run and fails when the line coverage is below 70 % (see [Continuous integration](#continuous-integration)); that figure includes the screens and the desktop head, so it is lower than the one above.

### Choices that differ from the criterion

- **No ORM.** Every name, key, text and time is encrypted per record and found through keyed MAC columns, so the repositories map the rows themselves with parameterised SQL.
- **No JSON.** The two apps exchange binary Noise frames padded to fixed sizes (see [`API.md`](API.md)), so the format of the data between them is not JSON; the app's own settings are plain text.
- **No OpenAPI, REST or HTTP status codes.** There is no HTTP interface; [`API.md`](API.md) is the description of the API.
- **The Noise handshake is our own implementation** of the published Noise specification (revision 34) on libsodium's primitives through NSec, checked against the published test vectors of cacophony and snow. No primitive is home-made.
- **No comments in the source code.** What a comment would explain is in the READMEs and in [`API.md`](API.md); only comments that a tool generated stay as it wrote them.

### Planned

| What | When | Why |
| --- | --- | --- |
| A GitHub Release with a Windows build, a Linux build and an APK | When the MVP is reached, and in any case before the review (2026-12-02) | Production deployment: a build that anyone can download and run |

## Prerequisites

- .NET SDK 10.0.404 (`global.json`). A later 10.0.4xx patch also builds, but its restore changes the Android head's lock file (see Continuous integration).
- For the Android head only: the android workload (`dotnet workload install android`), the Android SDK with the API 36.1 platform, and a JDK (Microsoft OpenJDK 21 is recommended). On a machine without them, `dotnet build src/P2PMessenger.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\Android\sdk -p:JavaSdkDirectory=C:\Android\jdk -p:AcceptAndroidSDKLicenses=true` installs both into the given folders (absolute paths without spaces). Then set the `ANDROID_HOME` and `JAVA_HOME` environment variables to those folders so later builds find them.
- On Windows, NSec's installation notes say that the native libsodium it loads needs the Microsoft Visual C++ Redistributable (x64). A developer machine usually has it and a clean Windows installation may not, so the tests of `P2PMessenger.Core.Tests` may fail to load the library there.

- For tor in a build (optional, see [Tor](#tor)): PowerShell 7 or Windows PowerShell 5.1, gpg (Git for Windows includes one) and tar (part of Windows 10 1803 and later).

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
- The tests run on Microsoft Testing Platform (`global.json`). Every `dotnet test` run also writes one Cobertura coverage report for each test project, into a `TestResults` folder in the test assembly's output folder, or into the folder given by `--results-directory` (`tests/Directory.Build.props`). Reports of earlier runs stay there, so delete the folder before a run whose reports you want to merge (see [Tests and coverage](#tests-and-coverage)).
- Coverage instrumentation rewrites `P2PMessenger.Core.dll` next to the test assembly while the tests run, so the Core architecture test reads the copy that `P2PMessenger.Core.Tests.csproj` puts in the `architecture` subfolder on build.
- A test project that runs no tests ends with exit code 8, and then `dotnet test` fails; a new test project without tests needs `--ignore-exit-code 8` in its project file until its first test.
- The integration tests, the UI tests and the end-to-end tests open TCP connections on the loopback interface. The storage, application, integration and end-to-end tests create databases in the temporary folder and delete them afterwards.
- `P2PMessenger.Desktop.Tests` tests the key stores of the desktop head. Its DPAPI tests run only on Windows. Its Secret Service tests run only on Linux with an unlocked Secret Service on the D-Bus session bus, and are reported as skipped elsewhere (as on a CI runner, which has no session bus).
- `P2PMessenger.Android.Tests` tests the parts of the Android head that need no Android API (the stored format of the Android Keystore protector, its fallback from StrongBox to the TEE and its answers to failures) by compiling those source files of the head into itself, because a project cannot reference the head without the android workload. The one class that calls the Android Keystore is checked on a phone.
- A Debug APK from `dotnet build` holds only the Android part of the app and runs only when deployed with `dotnet build src/P2PMessenger.Android -t:Run`. To install it by hand, build it with `-p:EmbedAssembliesIntoApk=true`.
- Every restore writes a `packages.lock.json` file next to each project. Commit them. After changing a version in `Directory.Packages.props`, run `dotnet restore --force-evaluate` to update them.
- Every text the app shows is in `src/P2PMessenger.UI/Resources/UiText.resx`, with its class `UiText.Designer.cs` checked in next to it. Visual Studio and Rider regenerate the class when the `.resx` is saved; elsewhere, after editing the `.resx`, run `dotnet msbuild src/P2PMessenger.UI -t:GenerateResourceDesigners` (a target in `Directory.Build.targets`). A test fails when the class and the `.resx` differ.
- Avalonia's build tooling sends anonymous build-time telemetry to Avalonia, and the .NET CLI sends its own; neither affects the app. Set `AVALONIA_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` to turn them off.

## Testing

The tests are in `tests/`, one project per layer, with the folders of the project they test. Test classes are named `<Subject>Tests` and test methods `Subject_Condition_Result`.

| Layer | Project | What the tests check |
| --- | --- | --- |
| Unit: Core | `P2PMessenger.Core.Tests` | The published test vectors (RFCs, and the Noise vectors of cacophony and snow), reference vectors of our own formats, bit-flip sweeps that every contact code, announcement, message and session frame must survive by being refused, and the architecture rules |
| Unit: Storage, Application, Transport | `P2PMessenger.Storage.Tests`, `P2PMessenger.Application.Tests`, `P2PMessenger.Transport.Tests` | Common and edge cases of each class: a real SQLite database in a temporary folder with its migrations and per-record encryption, the services behind the screens, admission limits and the control of tor against a fake tor |
| Integration | `P2PMessenger.Integration.Tests` | Two complete apps (storage, application, transport) over real TCP on the loopback interface: identities, contacts, chat, restart, deletion, passphrase and lock; adversarial peers (strangers, wrong keys, tampered, truncated, replayed or reordered frames) and flooding, which are refused or closed; the dependency rules between the projects |
| UI | `P2PMessenger.UI.Tests` | The real views and view models on a real messenger, rendered with Avalonia.Headless: pages, forms and their validation, error and loading states, and the QR code read back from the rendered window |
| End to end | `P2PMessenger.E2E.Tests` | Two or three complete apps, each with its real main window, its encrypted SQLite database and its own TCP listener, driven only by mouse clicks and typed text: two people create identities, add each other by contact codes and chat until the sender's message shows "Received by the contact's app"; a message to a contact whose app is closed waits and arrives when the app starts again; a stranger is refused, both as a raw connection to the listening port and as an app that holds only a contact's code; the passphrase lock hides the chat while messages keep arriving, a wrong passphrase is refused and a restart receives nothing until the passphrase is entered; a deleted chat disappears on this device only; deleting all data erases the database and returns to identity creation |
| Heads | `P2PMessenger.Desktop.Tests`, `P2PMessenger.Android.Tests` | The key stores and the platform code that needs no device |

- **Tools.** xUnit v3 on Microsoft Testing Platform, `coverlet.MTP` for Cobertura coverage reports, Avalonia.Headless for the UI and end-to-end tests, and ReportGenerator, pinned in `.config/dotnet-tools.json`, to merge the reports. Test doubles that stand in for the network are in `tests/Shared`: a fake tor SOCKS route, an in-memory hub for finding contacts on the local network, and a fake clock.
- **API endpoints.** There is no HTTP API (see [Back-end](#back-end)). What the integration tests exercise in its place is the encrypted protocol between two apps and the application interface that the screens call, both described in [`API.md`](API.md), the SQLite database, and the access control: callers that are not contacts are refused, and the passphrase lock stops all sending and receiving.
- **How to run.** `dotnet test --solution P2PMessenger.Desktop.slnf`; the end-to-end tests alone, which take under a minute, with `dotnet test --project tests/P2PMessenger.E2E.Tests`. The commands for one merged coverage figure and its HTML report, and the figures themselves, are in [Tests and coverage](#tests-and-coverage). CI runs the same suite on Windows and Linux for every push and pull request, and a failing test fails the job (see [Continuous integration](#continuous-integration)).
- **Known gaps and why.**
  - The Android head is not in the coverage figure: a test project cannot reference it without the android workload, and running it needs a phone, which CI does not have (CI builds it). Its parts that need no Android API are compiled into `P2PMessenger.Android.Tests`; the class that calls the Android Keystore is checked on a phone.
  - The key stores of the desktop head are tested where their platform exists: the DPAPI tests run on Windows only, and the Secret Service tests run on Linux only with an unlocked Secret Service on the session bus, which a CI runner lacks, so they are skipped there. The code that talks to a real camera and the desktop window needs a camera and a display, so tests cover it only in part, and it is checked on the machines themselves.
  - Tests with a real tor run only when `P2PM_TOR` names a tor executable, and the one over the real Tor network also needs `P2PM_TOR_NETWORK=1` and the internet. In CI, apps reach each other through the fake tor route instead.
  - The UI and end-to-end tests run on Avalonia's headless platform: they draw the real views and send real mouse and keyboard input, but no test drives a window of the operating system, a camera or an Android phone. The end-to-end tests stand in for the platform key store with a fake one, and for multicast and broadcast, which need real network interfaces, with the in-memory hub of `tests/Shared`; everything else, including the TCP connections, the Noise sessions and the encrypted database, is real.

## Trying the app

To try two apps:

1. Start two builds on two devices on one Wi-Fi network or one phone's hotspot, or both on one PC (`dotnet run --project src/P2PMessenger.Desktop`, or the Android head deployed with `dotnet build src/P2PMessenger.Android -t:Run`). Two apps on one PC need their own data folders: start the second with `dotnet run --project src/P2PMessenger.Desktop -- --data <folder>` (see [Database](#database)).
2. In each, create an identity, with a passphrase or without one (a passphrase is needed on a Linux desktop without a Secret Service). With a passphrase, the app asks for it at each start and after 5 minutes in the background; "Lock" in the header locks it at once.
3. In person: in each, open "Add in person", which shows your QR code, and choose "Scan their code" to scan the other screen with the camera (on a PC, a window with the webcam's picture opens). A scanned contact is added at once with its key checked by you; the page keeps your code on screen so the other person can scan it too. A device without a usable camera says so and offers the text or file code instead.
4. Through another messenger: in each, open "My code", copy the code, save it to a file or save the QR code as an image, and pass it to the other app. In the other app, open "Add contact", paste the code or open the file or the image, and choose "Check code" and "Add contact". Such a contact starts as "key not compared", and the app asks you to compare fingerprints.
5. Nothing else is needed: once both have added each other, the apps find each other on the network within a few seconds. When the first connection succeeds, both apps say that the contact is added and that the app checked the key, not the person, and the contact list shows the contact as "Reachable nearby". Compare the fingerprints under "Fingerprints" for a contact added from a code, then send messages. Each of your messages shows "Waiting" until the other app confirms it and "Received by the contact's app" after that; select the state to see in one line what it confirms. Close one app, send from the other, and start the first again: the waiting messages go out once the two have found each other again.

On Windows, the firewall asks on the first run whether the app may accept connections and datagrams; allow it for the network you use (a phone's hotspot counts as a public network). If the contact's device is heard but the connection fails, its conversation lists the possible reasons. Debug builds have a "Network (debug)" page that shows the device's local interfaces, its ports and where each contact was last heard.

## Finding contacts on the local network

Each app listens for its contacts on a TCP port chosen at random from 49152 to 65535 each time it starts, so that trying one port does not find the app's users. It tells its contacts the port in private announcements, which it sends every 3 seconds or so on every local interface, to the multicast group 239.255.83.5 and to the interface's broadcast address, both on UDP port 45821. Interfaces of mobile data and VPNs are left out, so announcements stay on the local network.

- An announcement is 274 bytes: a fresh 16-byte random value, the TCP port and 32 slots of 8 bytes. A contact's slot is a keyed BLAKE2b value under a key that only the two of them share, so only that contact recognises it; unused slots are random, and a device with more than 32 contacts sends several announcements of the same size. It holds no name, key, contact list or message, and two announcements share nothing that links them. Others on the network can still see that a device sends announcements.
- An app that recognises a contact's announcement connects to the address it came from and the port in it, unless the two are already connected. Both sides do so, and the connection rules keep one session. A contact counts as reachable only once its key has been checked in that session, never because of an announcement alone, and stops being dialled 30 seconds after its last announcement.
- When no contact has been heard for 30 seconds, the app also sends its announcements to every address of each local subnet of at most 510 addresses, paced and at most once a minute, for networks that drop multicast and broadcast.
- Each app screens at most 100 announcements a second, and 10 a second from one address, and ignores its own and repeated ones.
- On Android the app does not hold a multicast lock yet, so a phone that is a client of another network may hear only the broadcast announcements; a phone that shares its hotspot hears both.

## Tor

The app's internet route is Tor: C tor from the Tor Project's Tor Expert Bundle, the same build on every platform. The bundles are not in the repository. `pwsh tools/get-tor.ps1` (or `powershell -ExecutionPolicy Bypass -File tools\get-tor.ps1` in Windows PowerShell) downloads the pinned bundles from the Tor Project's archive into `tools/tor`, checks each one's OpenPGP signature against the Tor Browser Developers' key `EF6E286DDA85EA2A4BA7DE684E2C6E8793298290` and its SHA-256 against the value pinned in the script, and unpacks it. Without options it gets the bundle of the desktop it runs on (windows-x86_64 or linux-x86_64) and the three Android ones; `-Platform` chooses others (`all` for every pinned one), and `-Force` downloads them again. The pinned bundles are those of Tor Browser 16.0.1 (tor 0.4.9.16) for the desktops and 16.0.2 for Android, a release for Android only that fixed how 16.0.1's Android tor was linked.

- A build works without the bundles, and CI builds without them. The desktop build copies the `tor` folder of its platform's bundle next to the app (`tor/tor.exe` or `tor/tor`). The Android build is made for 32-bit ARM, 64-bit ARM and x86_64 (`RuntimeIdentifiers`), so one APK installs on 32-bit phones too, and packs the `libTor.so` of the armeabi-v7a, arm64-v8a and x86_64 bundles as native libraries, which Android extracts at install (`android:extractNativeLibs`), because the app starts tor as a program; there is no tor for 32-bit x86. An app without tor says so on its contact list.
- When the messenger starts, after the passphrase if one is set, the app starts tor as a separate process with only its own settings: empty configuration files, a data folder of its own (`tor` inside the app's data folder, which on Android is the app's folder that is not backed up), a control port and a SOCKS port on 127.0.0.1 that tor chooses, and cookie authentication. The app reads the control port from a file that tor writes, sends the cookie only after the control port names the cookie file that the app chose, takes ownership of tor so that tor exits with the app, and follows tor's status events. tor's own log is off (`--quiet`), and the app logs only tor's state, never an address.
- The app adds its onion service with `ADD_ONION` each time tor starts, giving tor the onion-service key from the encrypted database, so tor's data folder never holds the service key. tor hands the connections to the onion service to the app through a Unix socket in tor's data folder on Linux and Android, which other apps cannot open, and through a port on 127.0.0.1 chosen at random on Windows, whose tor has no Unix sockets. Every such connection still has to pass the same handshake as on the local network, so a program on the device that connects to the port gets nowhere; connections from the onion service have no address and share the overall limits against flooding.
- The contact list shows one line about the internet route: connecting to Tor with tor's progress, connected to Tor, or unavailable with the reason when the app knows it (tor is not part of this build, the device has no network, its date or time is wrong, the Tor network cannot be reached, tor could not start, tor stopped), and that nearby messaging continues. The app starts tor again 30 seconds after it fails or stops. Debug builds show tor's state, whether the onion service was published and the kind of socket on "Network (debug)".
- The app connects to its contacts' onion services through tor's SOCKS port, with a SOCKS5 client of its own (RFC 1928, with username and password authentication from RFC 1929). Every attempt uses a new random username, so tor builds a new circuit for it. While the line says "Connected to Tor", the app keeps every contact connected over Tor, also a contact who is still waiting for the first connection: an attempt has 30 seconds, a failed one is tried again at once on a new circuit, and then after 5 seconds, doubling up to 60; a contact who comes online also connects to the other side. Over Tor a caller has 10 seconds for its hint and 20 for the handshake.
- The app keeps one session per contact. When a contact is reachable both over Tor and on the local network, a Tor session replaces the local one; a local session replaces a Tor session only when the Tor session is at least 10 seconds old, which happens when the other side has lost it. The local network keeps finding and connecting the contact independently, so it carries the messages as soon as no Tor session is open: when tor is not running, when the contact's onion service does not answer, or when a Tor session closes. A Tor session that stalls without closing holds the messages until it is closed after 45 seconds without a frame.
- When the device moves to another network (Wi-Fi to mobile data, another Wi-Fi, a cable, a VPN that comes or goes), the app tells tor about 2 seconds after it notices the change, which takes up to 5 seconds: tor drops its old connections, so the Tor sessions end and both sides connect again, builds new circuits over the new network and keeps the onion service. With no network at all tor pauses until a network returns. A new mobile address without such a switch is left to tor, which notices dead connections only after its own, longer timeouts.
- When two people add each other, the first connection goes over whichever route answers first, so two people on one network finish within seconds over the local network, and a Tor session replaces it later.
- The contact list and the conversation show "Reachable over Tor" or "Reachable nearby" for the route of the open session. Failed Tor attempts give no reasons there; Debug builds show their state per contact on "Network (debug)". The app logs only the kind of a failed attempt, never an onion address.
- Tor can be turned off: identity creation asks whether to use Tor (checked unless you uncheck it), and a "Turn off Tor" / "Turn on Tor" button next to the line on the contact list switches it at any time. While it is off, tor does not run, its data folder is deleted, the line says "Internet route off", and messages go only over the local network. The choice is kept in the app's settings (Android's preferences, `settings.txt` in the desktop data folder).
- "Delete all data" also deletes tor's data folder and turns Tor back on for the next identity.
- Tests that need a real tor run only when `P2PM_TOR` names a tor executable (for example `P2PM_TOR=/usr/bin/tor dotnet test --solution P2PMessenger.Desktop.slnf`) and are reported as skipped otherwise; they start tor with its network turned off. `RealTorNetworkTests` connect two apps over the real Tor network, each with its own tor, and run only when `P2PM_TOR_NETWORK=1` is set as well and the machine has internet. The other tests of the Tor route connect apps through a fake SOCKS5 proxy (`tests/Shared/FakeSocksProxy.cs`) that passes the bytes on to each app's real onion listener.

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
sqlite3 :memory: ".read src/P2PMessenger.Storage/Migrations/0001_initial.sql" ".read src/P2PMessenger.Storage/Migrations/0002_message_id.sql" ".read src/P2PMessenger.Storage/Migrations/0003_outgoing_delivery.sql" ".read src/P2PMessenger.Storage/Migrations/0004_sent_sequence.sql" ".tables"
```

Give a file name instead of `:memory:` to keep the database, for example to open it in DB Browser for SQLite; delete the file afterwards, it is not part of the repository.

### Where the app keeps its data

The Windows and Linux app keeps its database, `messenger.db`, in `%LOCALAPPDATA%\P2PMessenger` on Windows and in `~/.local/share/P2PMessenger` on Linux (`$XDG_DATA_HOME/P2PMessenger` when that is set). `--data <folder>` (or `--data=<folder>`) on the command line uses another folder. Every name, key, text and time in it is encrypted with a random data key. The Android app keeps it in a `P2PMessenger` folder inside the app's folder that Android does not back up. The data key is wrapped by the platform's key store: DPAPI for the current Windows user, a Secret Service such as GNOME Keyring or KWallet on Linux, or, on Android, a non-exportable AES-256 key in the Android Keystore, kept in StrongBox where the phone has one and in the TEE otherwise, and usable while the phone is locked, so that the app can open its data when Android starts it in the background. A passphrase, chosen when the identity is created or later on the "Passphrase" page, adds a second layer: then the app needs both the passphrase and the key store. A copy of the folder cannot be read on another computer, phone or user account, even with the passphrase.

The passphrase needs at least 12 characters; there are no other rules, and a forgotten passphrase cannot be recovered. It can be changed or, where a key store exists, removed. The database is created only when the identity is created, with the protection chosen there. On a Linux desktop without a Secret Service the app asks for a passphrase and writes nothing without one; the passphrase alone then protects the data. Data that an earlier Android build saved under the passphrase alone (before the app used the Android Keystore) still opens with it; removing the passphrase moves the key into the Keystore, and setting one again puts it under both. If the app cannot open its data (the key store is locked or unreachable, the folder was copied from elsewhere, the data was changed or comes from a newer version), it shows why and offers to try again or to delete all data and start over; it never replaces data it cannot read on its own.

Deleting a chat removes its messages from the database and cancels the ones still waiting; deleting all data first erases every row, the data key included, then has the key store forget its secret and deletes the database files, after which the app starts over with identity creation. Both run with `secure_delete` and empty the journal, so the database files keep none of the deleted values. The app cannot overwrite the storage hardware, though: on flash memory, old copies of a file's blocks can stay on the drive until it reuses them. Where the Linux Secret Service or the Android Keystore held the key or its share, such copies stay unreadable once it has forgotten that secret. DPAPI keeps no secret to forget, so on Windows such copies could be opened with forensic tools by someone who can sign in as the same Windows user (and knows the passphrase, if one was set); where a passphrase alone protected the data, by someone who knows it. Contacts' apps keep their own copies of the chats.

## Running in the background on Android

While its data is open, the Android app runs a foreground service of the `connectedDevice` type with a notification that stays while it runs, so that its listener, its announcements and its waiting messages keep going with the screen off. The app starts the service only while it is visible, because Android does not let an app start one from behind the lock screen; after a restart of the phone or an update of the app, the app starts it again by itself if it was running and the user had not quit, and if a passphrase is set it shows a notification that asks the user to open the app and unlock it instead. "Quit" on the notification opens a confirmation, and confirming stops the service, the connections and the app; the "Background" page offers the same. While the service runs, the app holds a Wi-Fi multicast lock, so that the announcements sent by multicast reach it on a Wi-Fi network.

The "Background" page, shown once after the first start and later from the contact list, asks Android to exempt the app from battery optimisation; if the user refuses, it says that messages may be delayed while the phone is idle, and the app keeps working. If the app's battery use is set to Restricted, Android does not let the service run in the background at all; the page then says that the app stops soon after you leave it and offers to open the app's settings. Opening the app starts the service again if Android has stopped it. It also asks for the notification permission on Android 13 and later. A message that arrives while the app is not on the screen shows a notification with the sender's name; the text is shown only after "Show message text in notifications" is turned on (it is off at first). Whatever Android's lock-screen setting, the message notification itself is hidden on a locked phone, and a separate notification on the lock screen says only "New message", without the sender; it goes away when the phone is unlocked.

How long a phone stays reachable with the screen off depends on the phone and the network and is measured, not promised: a phone that is a Wi-Fi client can stop answering incoming connections after a while with the screen off. In the author's screen-off runs on a Pixel 7a (2026-11-24), allowing the app to run in the background, the battery exemption that the "Background" page asks for, was what kept it reachable; a partial wake lock and alarm wake-ups, tried as measurement options before, were not needed and were removed. Debug builds write a log of the service to the app's `files/diagnostics` folder: times, how late the service's one-minute timer ran, how long the processor slept, the idle, screen, battery and power-saver states and the app's own log lines, with no names, texts, keys or addresses. `adb exec-out run-as io.github.kaunazdagaz.p2pmessenger ls files/diagnostics` lists the files and `cat` reads one.

## Android permissions and backup

The app declares these permissions. `INTERNET` lets it open network connections at all: the connections to contacts and the announcements that find them on the local network. `CAMERA` lets it read the other person's QR code on "Add in person"; Android asks for it the first time "Scan their code" is chosen, and the app keeps no picture. `FOREGROUND_SERVICE` and `FOREGROUND_SERVICE_CONNECTED_DEVICE` let it run the background service described below, and `CHANGE_WIFI_MULTICAST_STATE` lets it hold the Wi-Fi multicast lock, which is also what Android 14 and later require before a service of the `connectedDevice` type may start. `POST_NOTIFICATIONS` lets it show the service's notification and the message notifications; Android 13 and later ask the user for it. `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` lets it ask Android to exempt it from battery optimisation, and `RECEIVE_BOOT_COMPLETED` lets it start again after the phone restarts. Two more come with libraries: `ACCESS_NETWORK_STATE`, from AndroidX Media3, which the CameraX view brings in (a permission Android grants at install, which lets code read whether a network is connected), and a permission of AndroidX Core, limited to apps signed with the same key, that keeps the receivers the app registers at run time private. While the app targets API 36, Android grants access to the local network with `INTERNET`, so the app neither declares nor asks for `ACCESS_LOCAL_NETWORK`.

Only the main activity is exported, because the launcher starts it; the camera screen, the background service, the quit confirmation and the receiver for the restart of the phone and the update of the app are not, and Android still delivers its own restart and update broadcasts to a receiver that is not exported. The settings of the background page live in two small files of shared preferences that hold only on and off values (message text in notifications, whether the page was offered, whether the service should start again), never names, keys or texts. AndroidX's profile installer adds an exported receiver meant only for developer tools, which the app's manifest removes. Android neither backs up nor transfers the app's files: `android:allowBackup` is off, and the data extraction rules (`Resources/xml/data_extraction_rules.xml`) exclude every folder from cloud backup and from device-to-device transfer, which Android 12 and later would otherwise still allow. The Keystore key never leaves the phone in any case, so restored files could not be read.

## Continuous integration

GitHub Actions runs `.github/workflows/ci.yml` on every push and pull request. The actions are pinned to commit SHAs, and the workflow can only read the repository.

- **Desktop**, on Windows and Linux: a locked restore, the format check, a Release build (warnings are errors) and the tests with coverage. The coverage reports are uploaded as the `test-results-<OS>` artifact. On Linux the job also fails if `dotnet list package --vulnerable --include-transitive` finds a vulnerable package.
- **Coverage**, on Linux, once the desktop jobs have passed: ReportGenerator, pinned in `.config/dotnet-tools.json`, merges the reports of the Linux run. The job summary shows the coverage by assembly, the `coverage-report` artifact holds the HTML report. The job fails when the line coverage of the desktop solution's assemblies, test assemblies left out, is below 70 %. GitHub's own code-coverage feature is not used: it needs GitHub Code Quality, which is offered to organizations on the Team and Enterprise Cloud plans.
- **Android**, on Linux: Microsoft OpenJDK 21 (the newest JDK the android workload supports), the android workload and the Android SDK parts the project needs, then a locked restore, a Debug build with the .NET code inside the APK, and the same check for vulnerable packages.
- For pull requests and `main`, the desktop builds and the APK are uploaded as artifacts. Each run signs the APK with a new debug key, so uninstall the previous build before installing another one.

Dependabot (`.github/dependabot.yml`) proposes updates once a week for the NuGet packages (all of them in one pull request), the .NET SDK in `global.json` (within .NET 10) and the actions. It skips xunit.v3.mtp-v2 4.x, which does not work with Avalonia 12.1.5 yet.

The Android head's lock file records `Microsoft.NET.ILLink.Tasks`, whose version comes with the .NET SDK, so CI has to use the SDK that wrote the lock file: `global.json` pins it with `rollForward: patch`, and CI installs exactly that version. To move to a newer SDK, change `global.json`, run `dotnet restore P2PMessenger.slnx --force-evaluate` with that SDK, and commit both. A Dependabot pull request that updates the SDK fails the Android job until its branch gets the updated lock file.

## Licence

Not chosen yet.
