## Milestone 1: The MVP (Minimum Viable Product)

The goal of the MVP is a functional, albeit minimal, application that proves the core concept.

### Core Engine (`Axorith.Core`)
- [x] Design SDK (`IModule`, settings, context, etc.)
- [x] Implement Module Loader (dynamic DLL loading)
- [x] Implement Module Registry (in-memory cache)
- [x] Implement Preset Manager (JSON serialization)
- [x] Implement Session Manager (orchestration logic)
- [x] Configure Dependency Injection and logging (`AxorithHost`)

### Shared Libraries (`Axorith.Shared`)
- [x] Create `Shared.Utils` for common functions.
- [x] Create `Shared.Exceptions` for custom error types.

### Test Module (`Axorith.Module.Test`)
- [x] Implement a full-featured test module that uses every aspect of the SDK.

### Client Application (`Axorith.Client`)
- [x] Basic project setup with Avalonia and ReactiveUI.
- [x] Implement navigation shell (`ShellViewModel`).
- [x] **Dashboard View (`MainView`):**
    - [x] Load and display list of saved presets.
    - [x] Implement `Edit` button (open editor with existing preset).
    - [x] Implement `Delete` button.
    - [x] Implement `Start` button (call `SessionManager.StartSessionAsync`).
    - x ] Display session status (e.g., "Session 'Coding' is active").
- [x] **Session Editor View (`SessionEditorView`):**
    - [x] Load available modules from `ModuleRegistry`.
    - [x] Add/Remove modules from a preset.
    - [x] Dynamically render settings UI based on `GetSettings()`.
    - [x] Implement `Save` functionality (call `PresetManager.SavePresetAsync`).
- [ ] **Error Handling:**
    - [ ] Display user-friendly notifications for exceptions (e.g., `SessionException`).

---

## Milestone 2: The "Useful" Version

Once the MVP is stable, the focus shifts to creating real value with first-party modules.

- [x] **Module: Site Blocker:** A module to block websites by modifying the `hosts` file or using platform-specific APIs.
- [x] **Module: Application Launcher:** A module to start and stop applications (`.exe`, `.app`, etc.).
- [ ] **Module: Spotify (Basic):** A module that can start/stop playback of a specific playlist using the Spotify Web API.
- [ ] **UI Polishing:** Improve the look and feel of the Client. Add animations and better visual feedback.
- [ ] **Installer:** Create installers for Windows (MSIX), macOS (DMG), and Linux (AppImage/Flatpak).

---

## Milestone 3: The "Ecosystem"

The focus shifts to community, automation, and advanced features.

- [ ] **Session Scheduling:** A UI to automatically start sessions based on time or day of the week.
- [ ] **Module Marketplace:** A view within the app to discover and install third-party modules from a central repository.
- [ ] **Advanced Module SDK:** Add capabilities for modules to provide custom UI, report progress, and interact with each other.
- [ ] **More Integrations:** Slack, Telegram, VS Code, etc.

---

## Long-Term Vision (The "OS")

- [ ] **Cloud Sync:** Synchronize presets and settings across multiple devices.
- [ ] **Mobile Clients:** Small companion apps for iOS/Android to remotely start/stop sessions.
- [ ] **Analytics:** Provide insights into your deep work habits.
- [ ] **Corporate Integrations:** Modules for enterprise tools like Jira, Teams, etc.