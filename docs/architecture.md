# Axorith Architecture

This document describes the current architecture of Axorith: all major projects, how they interact, and the design rules that keep the system stable and extensible.

It is intended for contributors who want to:

- understand how the system is split into layers,
- add new modules or features without breaking the architecture,
- debug end‑to‑end flows (from UI down to modules and browser extensions).

The document reflects the **current** tech stack:

- **.NET 10.0**, **C# 14**
- **Avalonia UI** + **ReactiveUI** for the desktop client
- **ASP.NET Core gRPC** for the host
- modular architecture built around `Axorith.Module.*` plugins

---

## Core Philosophy

The architecture is based on three core principles:

1. **Modularity** – all functionality is implemented as isolated modules (plugins). The Core engine knows nothing about concrete features; it only understands `IModule` contracts.
2. **Decoupling** – the UI (`Client`) is completely separated from business logic (`Core`/`Host`). Modules are separated from Core. Contracts live in `Axorith.Sdk`.
3. **Testability** – every important part of the system can be tested in isolation via dependency injection and interface‑first design.

These principles are codified as "Golden Rules" in `CONTRIBUTING.md` and enforced during code review.

---

## High‑Level Architecture

This section shows how the **projects actually depend on each other** according to the `.csproj` files, and how that maps to runtime responsibilities.

```text
                                +-------------------------+
                                |   Browser (Firefox)     |
                                |   Axorith Extension     |
                                +------------+------------+
                                             ^
                                             | native messaging
                                             v
                                      +------+------+
                                      | Axorith.Shim|
                                      | (native host|
                                      |  process)   |
                                      +-------------+

+---------------------------+           +---------------------------+
|       Axorith.Client      |   gRPC    |       Axorith.Host        |
|  (Avalonia UI, Reactive)  | <=======> |   (ASP.NET Core gRPC)     |
+--------------+------------+           +---------------+-----------+
               |                                        |
               | ProjectReference                       | ProjectReference
               v                                        v
     +---------+------------+                +----------+-----------+
     |     Axorith.Core     | <------------> |   Axorith.Contracts  |
     |   (session engine,   |    uses gRPC   | (generated gRPC      |
     |   module orchestration)|  contracts   |  contracts, messages)|
     +----------+-----------+                +----------+-----------+
                |                                         ^
                | ProjectReference                        |
                v                                         |
      +---------+-------------------------------+         |
      |               Axorith.Shared            |         |
      |  (Platform / Utils / Exceptions /       |         |
      |   ApplicationLauncher sub‑projects)     |         |
      +----------------------+------------------+         |
                             ^                            |
                             |                            |
                             | ProjectReference           |
                             |                            |
   +-------------------------+----------------+     +-----+-----------------+
   |        Axorith.Modules (plugins)         |     |      Axorith.Sdk      |
   |  (ApplicationLauncher, JBIDELauncher,    |---->|  (IModule/ISetting/    |
   |   SiteBlocker, SpotifyPlayer, ...)       |     |   IAction contracts,   |
   |                                         |---->|   ValidationResult,    |
   |  depend on:                             |     |   Platform, etc.)      |
   |    - Axorith.Sdk                        |     +------------------------+
   |    - Axorith.Shared.*                   |
   +-----------------------------------------+
```

**Key roles in code and dependencies:**

- `Axorith.Sdk` – pure contract layer (interfaces, enums, immutable models). Referenced by:
  - `Axorith.Core`, `Axorith.Shared.*`, `Axorith.Modules.*`, `Axorith.Contracts`, `Axorith.Client`, `Axorith.Host`.
- `Axorith.Shared.*` – toolbox libraries (Platform, Utils, Exceptions, ApplicationLauncher). Referenced by:
  - `Axorith.Core`, `Axorith.Modules.*`, `Axorith.Shared.Tests`, and in some cases indirectly by `Host` via `Shared.Platform`.
- `Axorith.Core` – engine for sessions/modules; referenced by:
  - `Axorith.Host`, `Axorith.Client`, `Axorith.Core.Tests`, `Axorith.Host.Tests`, `Axorith.Integrations.Tests`.
- `Axorith.Contracts` – gRPC contracts and generated code; referenced by:
  - `Axorith.Host`, `Axorith.Client`, `Axorith.Contracts.Tests`, `Axorith.Host.Tests`, `Axorith.Integrations.Tests`.
- `Axorith.Host` – ASP.NET Core gRPC host; references `Core`, `Contracts`, `Sdk`, `Shared.Platform`.
- `Axorith.Client` – Avalonia UI; references `Sdk`, `Core`, `Contracts`, `Shared.Exceptions`.
- `Axorith.Modules.*` – plugin projects; each references `Sdk` and a subset of `Shared.*` (Utils, Exceptions, Platform, ApplicationLauncher).
- `Axorith.Shim` – standalone native host process; does not reference other Axorith projects but is used by SiteBlocker + browser extension at runtime.

At runtime, the main interaction path remains:

- **Client ⇄ Host** over gRPC (`Axorith.Client` ⇄ `Axorith.Host` via `Axorith.Contracts`).
- **Host → Core → Shared** for all session/module logic and platform access.
- **Modules → Sdk + Shared** when Core instantiates them.
- **Shim ⇄ Browser** for the SiteBlocker flow via native messaging.

### Client–Host–Core Interaction Diagram

The following sequence‑style diagram shows how a typical client operation flows through the system without going into module internals:

```text
User
  │  clicks "Start Session"
  ▼
Axorith.Client (MainViewModel)
  │  call SessionsApi.StartSession(presetId)
  ▼
Axorith.Host (gRPC endpoint)
  │  resolve ISessionManager
  │  load SessionPreset via IPresetManager
  ▼
Axorith.Core (SessionManager)
  │  for each ConfiguredModule in preset
  │    ├─ IModuleRegistry.CreateInstance(moduleId)
  │    ├─ apply settings (ISetting.SetValueFromString)
  │    ├─ ValidateSettingsAsync
  │    └─ OnSessionStartAsync
  ▼
EventAggregator / Broadcasters
  │  publish SessionStarted + initial SessionSnapshot
  ▼
Axorith.Host (gRPC streaming)
  │  push events and snapshots to connected clients
  ▼
Axorith.Client
  │  update ViewModels (e.g., active session, module statuses)
  ▼
User sees updated UI
```

This flow highlights that the Client never talks to modules or Core directly – it always goes through the Host’s gRPC API.

### Shim / Native Messaging Interaction Diagram

For browser integration the system uses a dedicated native host process and the browser’s native messaging protocol:

```text
Axorith.Module.SiteBlocker
  │  writes JSON commands
  │  to Named Pipe "axorith-nm-pipe"
  ▼
Axorith.Shim (native host)
  │  reads from Named Pipe
  │  wraps payload with length prefix
  │  writes to STDOUT
  ▼
Browser (Firefox)
  │  native messaging channel
  │  delivers JSON to extension
  ▼
Site Blocker Extension
  │  updates browser.storage.local
  │  injects content.js / blocked.html
  ▼
User’s tabs reflect Focus Mode
```

This diagram omits module‑internal details and focuses only on process and protocol boundaries.

---

## Solution Layout

The solution is organized as follows:

```text
src/
  Sdk/                 -> Axorith.Sdk (contracts)
  Core/                -> Core services (engine)
  Host/                -> gRPC host
  Client/              -> Avalonia UI
  Shared/              -> Shared libs (Platform, Exceptions, Utils)
  Modules/             -> Axorith.Module.* (plugins)
  Contracts/           -> gRPC .proto + generated code
  Shim/                -> Native messaging host (Axorith.Shim)

tests/
  Axorith.Sdk.Tests/
  Axorith.Core.Tests/
  Axorith.Shared.Tests/

extensions/
  firefox/             -> Site Blocker browser extension
```

Build is centralized via `Directory.Build.props` / `Directory.Build.targets` and publishes modules to:

```text
build/<Configuration>/modules/<ModuleName>/
  Axorith.Module.<Name>.dll
  Axorith.Module.<Name>.pdb (if present)
  dependencies
  module.json
```

This directory is the discovery root for `ModuleLoader`.

---

## Axorith.Sdk ("The Law")

`Axorith.Sdk` defines the public contracts for the entire ecosystem. It is the "law" all other components must follow.

### Responsibilities

- Define `IModule` – the main plugin contract.
- Define `ISetting` / `Setting<T>` – reactive settings.
- Define `IAction` – non‑persisted commands.
- Define metadata types like `ModuleDefinition`, `ValidationResult`, `ValidationStatus`, `Platform`.
- Provide abstractions for HTTP and services that modules can depend on.

### Key Properties

- **Zero implementation logic.**
- **Zero external dependencies** beyond the base .NET library.
- Stable contracts, versioned carefully.

Example (simplified):

```csharp
public interface IModule : IDisposable
{
    IReadOnlyList<ISetting> GetSettings();
    IReadOnlyList<IAction> GetActions();

    Task InitializeAsync(CancellationToken cancellationToken) { ... }
    Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken);

    Type? CustomSettingsViewType { get; }
    object? GetSettingsViewModel();

    Task OnSessionStartAsync(CancellationToken cancellationToken);
    Task OnSessionEndAsync(CancellationToken cancellationToken);
}
```

Modules must treat `IModule` constructors as cheap and side‑effect‑free. Heavy operations belong in `InitializeAsync` or `OnSessionStartAsync`.

---

## Axorith.Core (Engine)

`Axorith.Core` is the headless engine that manages module lifecycle and deep‑work sessions.

### Main Services

```text
IModuleLoader   -> ModuleLoader
IModuleRegistry -> ModuleRegistry
IPresetManager  -> PresetManager
ISessionManager -> SessionManager
```

#### ModuleLoader

- Scans configured module search paths for `module.json`.
- Deserializes them into `ModuleDefinition`.
- Filters by `Platform[]` (using `EnvironmentUtils.GetCurrentPlatform()`).
- Validates the `assembly` field and existence of the DLL.
- Loads the DLL into a **collectible `AssemblyLoadContext`**.
- Locates a public, non‑abstract type implementing `IModule`.

#### ModuleRegistry

- Holds an in‑memory `Guid -> ModuleDefinition` map.
- `InitializeAsync` populates the map via `ModuleLoader`.
- `CreateInstance(Guid moduleId)`:
  - opens a new Autofac lifetime scope;
  - registers `ModuleDefinition` as scoped;
  - registers a module‑scoped `IModuleLogger` and `ISecureStorageService` wrapper;
  - registers the module type as `IModule` and resolves it.
- `Dispose()` unloads `AssemblyLoadContext` instances and runs GC to ensure they are collected.

#### PresetManager

- Persist `SessionPreset` to disk as JSON files in a directory resolved from Host configuration.
- Maintains a preset schema version and migrates older presets on load.
- Writes files atomically via temp file + `File.Move`.

#### SessionManager

`SessionManager` orchestrates the lifecycle of the current deep‑work session:

```text
SessionPreset
  └─ Modules: ConfiguredModule[]
       └─ Settings: key -> string value
```

Session startup flow:

```text
StartSessionAsync(preset)
  ├─ ensure no active session exists
  ├─ for each ConfiguredModule in preset:
  │    ├─ ModuleRegistry.CreateInstance(moduleId)
  │    └─ collect ActiveModule (Instance + Scope + Configuration)
  ├─ if no modules instantiated -> throw SessionException
  ├─ for each ActiveModule:
  │    ├─ apply saved settings via ISetting.SetValueFromString
  │    ├─ run ValidateSettingsAsync with timeout
  │    └─ run OnSessionStartAsync with timeout
  └─ on success -> raise SessionStarted event
```

On failure or cancellation:

- log the error;
- call `StopCurrentSessionAsync` (which runs `OnSessionEndAsync` with timeout and disposes modules/scopes);
- throw a `SessionException`.

Core is completely UI‑agnostic and does not know about gRPC or browser extensions.

---

## Axorith.Host (gRPC Host)

`Axorith.Host` is an ASP.NET Core application that hosts Core over gRPC.

### Responsibilities

- Configure Kestrel with HTTP/2 for gRPC (no TLS, loopback in MVP).
- Configure Serilog for structured logging.
- Use Autofac as the DI container.
- Expose gRPC services for sessions, presets, modules, diagnostics, and host management.

### Program.cs Overview

```text
WebApplicationBuilder
  ├─ UseSerilog(...)
  ├─ UseServiceProviderFactory(new AutofacServiceProviderFactory())
  ├─ Configure<HostConfiguration>(appsettings.json)
  ├─ ConfigureKestrel(Http2 + limits)
  └─ AddGrpc(...)

ContainerBuilder
  ├─ RegisterCoreServices()
  └─ RegisterBroadcasters()
```

`RegisterCoreServices` wires up:

- `ISecureStorageService` via `PlatformServices.CreateSecureStorage(logger)`;
- `ModuleLoader` as `IModuleLoader` (singleton);
- `ModuleRegistry` as `IModuleRegistry` with module search paths and allowed symlinks from `HostConfiguration`;
- `EventAggregator` for internal event publishing;
- `HttpClientFactoryAdapter` to bridge `System.Net.Http.IHttpClientFactory` to `Sdk.Http.IHttpClientFactory`;
- `PresetManager` as `IPresetManager` with its persistence directory;
- `SessionManager` as `ISessionManager` with timeouts from configuration.

`RegisterBroadcasters` registers:

- `SessionEventBroadcaster` – streams session events to clients;
- `SettingUpdateBroadcaster` – streams setting updates to clients;
- `DesignTimeSandboxManager` – manages design‑time module sandboxes.

The Host uses gRPC contracts from `Axorith.Contracts` (generated from `.proto` files) to define the public API.

---

## Axorith.Client (Avalonia UI)

`Axorith.Client` is the cross‑platform desktop UI built on Avalonia and ReactiveUI.

### Bootstrap

- `Program.cs` builds the Avalonia app and starts `App` with `ShutdownMode.OnExplicitShutdown` (closing the main window does not exit the process).
- `App.OnFrameworkInitializationCompleted`:
  - loads configuration from `appsettings.json` into `ClientConfiguration`;
  - builds a `LoggerFactory` (Logging section + console + Debug);
  - assembles a `ServiceCollection` (Microsoft.Extensions.DependencyInjection) with:
    - `ShellViewModel`, `LoadingViewModel`, `ErrorViewModel`;
    - `IWindowStateManager`, `IHostController`, `IHostTrayService`, `IConnectionInitializer`;
    - `Options<ClientConfiguration>`;
  - creates `MainWindow` with `ShellViewModel` as `DataContext` and initial `LoadingViewModel` content;
  - honours the `--tray` flag (start minimized to tray) and persists window state across runs;
  - initializes the tray icon via `HostTrayService`;
  - starts `IConnectionInitializer.InitializeAsync` in the background to connect to Host.

### MVVM Layer

Key ViewModels include:

- `ShellViewModel` – root VM that holds the current `Content` (Loading/Error/Main).
- `MainViewModel` – main application shell (presets, sessions, modules).
- `SessionEditorViewModel` – preset editor:
  - loads available modules from Host via `IModulesApi`;
  - manages `ConfiguredModule` instances inside a preset;
  - opens a settings overlay for a selected module.
- `SessionPresetViewModel` – view‑model wrapper over `SessionPreset` for display.
- `SettingViewModel` – bridges `ISetting` to Avalonia controls:
  - subscribes to `ISetting` reactive properties (`Label`, `IsVisible`, `IsReadOnly`, `ValueAsObject`, etc.);
  - on user edits, calls `IModulesApi.UpdateSettingAsync(moduleInstanceId, Setting.Key, value)`;
  - real values flow from Host back via streaming updates.
- `ActionViewModel` – wraps `IAction` for UI buttons and commands.

The Client intentionally keeps business logic minimal and delegates actual behavior to Host/Core.

---

## Axorith.Shared (Toolbox)

`Axorith.Shared` is a collection of small, focused libraries used by otherwise independent parts of the system:

- `Axorith.Shared.Exceptions` – custom exception types (e.g., `SessionException`).
- `Axorith.Shared.Platform` – platform‑specific abstractions:
  - `PlatformServices.CreateSecureStorage(ILogger)` → `ISecureStorageService` implementation for the current OS (Windows, Linux, macOS);
  - `PublicApi` – cross‑platform façade for window operations and native messaging integration;
  - OS‑specific implementations (`WindowsSecureStorage`, `LinuxSecureStorage`, `MacOsSecureStorage`, `WindowApi`/`LinuxWindowApi`/`MacOsWindowApi`).
- `Axorith.Shared.Utils` – utility helpers like `EnvironmentUtils.GetCurrentPlatform()`.

Each sub‑project in `Shared` must have a single, clearly defined purpose to avoid creating a monolithic "common" library.

---

## Axorith.Modules (Plugins)

Each module is a separate project `Axorith.Module.*` that:

- implements the `IModule` interface from `Axorith.Sdk`;
- depends only on `Sdk` and `Shared` libraries;
- **must not** reference `Core`, `Client`, or other modules.

Typical structure:

```text
src/Modules/<Name>/
  Axorith.Module.<Name>.csproj
  Module.cs              (IModule implementation)
  module.json            (static metadata)
```

`module.json` contains:

- `id` (Guid);
- `name`, `description`, `category`;
- `platforms` (array of `Platform` values);
- `assembly` (DLL containing the module implementation).

### SiteBlocker End‑to‑End Flow

The SiteBlocker module is a good example of a full stack flow:

```text
Session Start
  └─ Core starts Axorith.Module.SiteBlocker (IModule)
       ├─ constructor sets up settings and status field
       ├─ on Windows, reads axorith.json (native host manifest)
       ├─ registers the native messaging host for Firefox via PublicApi
       └─ OnSessionStartAsync:
            ├─ computes the list of domains to block
            └─ sends a JSON command over Named Pipe "axorith-nm-pipe"

Axorith.Shim (native host)
  ├─ listens on Named Pipe "axorith-nm-pipe"
  ├─ receives JSON from SiteBlocker
  └─ forwards it to the browser via native messaging protocol (stdout)

Browser Extension (Firefox)
  ├─ background.js receives "block" / "unblock" commands
  ├─ stores blocked domains in browser.storage
  └─ injects content.js / blocked.html into matching tabs
```

This flow demonstrates how a module remains isolated from Core and Client while still driving complex behavior via contracts and platform services.

---

## Axorith.Shim & Browser Extensions

### Axorith.Shim

`Axorith.Shim` is a minimal .NET process that acts as the native messaging host for the browser:

- runs a loop creating a `NamedPipeServerStream` on `"axorith-nm-pipe"`;
- reads JSON messages from the main Axorith process (SiteBlocker module) via the pipe;
- writes messages to stdout following the browser native messaging protocol:
  - 4‑byte little‑endian length prefix,
  - followed by UTF‑8 JSON payload;
- logs errors to `%AppData%/Axorith/logs/shim_error.log`.

The native host manifest `axorith.json` describes:

- `name` – the host name (e.g., `"axorith"`);
- `description` – human‑readable description;
- `path` – path to `Axorith.Shim.exe` (patched by the Windows installer);
- `type` – `"stdio"`;
- `allowed_extensions` – list of allowed browser extension IDs.

### Firefox Extension

The `extensions/firefox` folder contains the Site Blocker browser extension:

- `manifest.json` – MV3 manifest with `nativeMessaging`, `tabs`, `scripting`, and `storage` permissions.
- `background.js` –
  - connects to the native host via `browser.runtime.connectNative("axorith")`;
  - listens for `block` / `unblock` commands;
  - stores blocked domains in `browser.storage.local`;
  - injects `content.js` into matching tabs when blocking is active.
- `content.js` –
  - stops page loading via `window.stop()`;
  - replaces the DOM with the content and styles from `blocked.html`.
- `blocked.html` – overlay UI showing that Focus Mode is active and the site is blocked.

---

## Tests

Tests are split by layer:

- `Axorith.Sdk.Tests` – validation of SDK contracts (settings, validation, platform utilities).
- `Axorith.Core.Tests` – focused tests for core services such as `ModuleLoader`, `SessionManager`, `PresetManager`.
- `Axorith.Shared.Tests` – tests for shared utilities and platform helpers.

Example: `ModuleLoaderTests` create a temporary directory with a real `Axorith.Module.Test.dll` and a synthetic `module.json`, then assert that `LoadModuleDefinitionsAsync` discovers an `IModule` implementation correctly.

---

## Golden Rules (Summary)

To keep the architecture clean and maintainable, contributions must follow these rules:

1. **SDK is Law** – `Axorith.Sdk` contains only contracts, enums, and immutable models. No implementation logic or external dependencies.
2. **Core is Headless** – `Axorith.Core` must remain completely UI‑agnostic and unaware of gRPC or browser details.
3. **Modules are Isolated** – `Axorith.Module.*` projects may only reference `Sdk` and `Shared`. They must never depend on `Core`, `Client`, or other modules.
4. **Client is Dumb** – `Axorith.Client` focuses on presentation and user interaction. Business logic and session orchestration live in Host/Core.
5. **Dependency Injection Everywhere** – all services and ViewModels receive dependencies via constructors (prefer primary constructors). Avoid `new` for services.
6. **Async with Timeouts** – long‑running operations are asynchronous, take `CancellationToken`, and use appropriate timeouts (validation, startup/shutdown, I/O).
7. **Small, Focused Shared Libs** – each library in `Shared` must have a single, clear responsibility; avoid creating a giant "common" catch‑all.

Following these rules ensures that new features and modules can be added without compromising Axorith's stability and clarity.