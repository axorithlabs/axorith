# Axorith
### The Deep Work OS

> TL;DR: One click to enter deep work. Axorith automates your focus rituals.

---

## Our Philosophy: Your Mind is the Kernel

Axorith was born from a simple, powerful observation: **the modern digital workspace is fundamentally broken.**

Your focus — your most valuable asset — is constantly drained by tools that were never designed for deep work. The very tools meant for creation have become the primary source of friction. Existing applications only treat the symptoms: they track time, block sites, or manage tasks. They are features *within* the chaos.

**Axorith is not another application. It is an opinionated operating environment.** We believe your computer should be a silent, purpose-built instrument for your mind, not a battlefield for your attention.

Our engineering philosophy reflects this:

*   **Small, Optimized Core:** The engine of Axorith is a tiny, native, and ruthlessly efficient piece of engineering. We believe 10,000 lines of elegant code can do the work of a million.
*   **Radical Modularity:** We don't pretend to know your perfect workflow. Only you do. That's why the entire system is built on a clean, powerful SDK. Axorith provides the conductor's podium; you choose the instruments.
*   **The User as Root:** This is a tool for professionals and power users. It provides deep, granular control over your digital environment because we believe you should be the architect of your own focus.

We are building a thin abstraction layer between you and your machine — one designed to serve your flow, not fight it.

---

## The Problem: The "Focus Tax"

Every time you decide to do deep work, you pay a tax in time, energy, and willpower.

*   You manually open your IDE and project.
*   You hunt for your focus playlist.
*   You silence a dozen chat notifications.
*   You activate a site blocker.
*   You arrange your windows.

This 15-minute ritual is a barrier to entry. It's friction. It's just enough of a pain in the ass to make you procrastinate. You repeat it multiple times a day, and it drains you before you've even written a line of code.

## The Solution: One-Click Orchestration

Axorith eliminates this tax. It allows you to codify your entire workflow into a reusable "Session Preset."

**When you're ready to work, you press one button.**

Axorith executes your ritual in seconds. Your music starts, your tools launch, your distractions vanish. When you're done, one more click, and Axorith cleans everything up, returning your desktop to a state of calm.

It's your personal `init()` and `dispose()` script for your brain.

## Core Features

*   **Session Presets:** Design and save custom workflows for different types of work (e.g., "Coding," "Writing," "Design").
*   **Modular Architecture:** The entire system is built on plugins. Integrate with your favorite tools like Spotify, VS Code, Telegram, and more.
*   **Cross-Platform Core:** Built with .NET and Avalonia UI to run natively on Windows, macOS, and Linux.
*   **Developer First:** With a clean SDK, anyone can write their own modules to integrate with any tool that has an API (or can be scripted).

## Tech Stack

*   **.NET 9** & **C# 12**
*   **Avalonia UI** for the cross-platform user interface.
*   **ReactiveUI (MVVM)** for a modern, reactive UI architecture.
*   **Serilog** for structured logging.
*   **Dependency Injection** for a clean, decoupled, and testable Core.

## Project Architecture

Axorith is built on a clean, modular architecture to ensure stability, testability, and extensibility.

```
/src/
|
|--- Sdk/              # The public contract (IModule, etc.). The "Law" of Axorith.
|--- Core/             # The "engine". Headless, no-UI, manages sessions and modules.
|--- Client/           # The Avalonia UI application. The "control panel" for the engine.
|
|--- /Shared/          # Common libraries (Utils, custom Exceptions).
|    |--- Utils/
|    |--- Exceptions/
|
|--- /Modules/         # Home for all plugin implementations.
     |--- Test/
     |--- Spotify/      (Upcoming)
     |--- SiteBlocker/  (Upcoming)
```

## Getting Started (For Developers)

Interested in contributing or just running the project from source? Here’s how.

### Prerequisites

*   [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
*   An IDE like [JetBrains Rider](https://www.jetbrains.com/rider/) or [Visual Studio 2022](https://visualstudio.microsoft.com/) with the Avalonia plugin.

### Build & Run

1.  **Clone the repository:**
    ```sh
    git clone https://github.com/axorithlabs/axorith.git
    cd axorith
    ```

2.  **Restore dependencies and build the solution:**
    ```sh
    dotnet build
    ```
    This will build all projects and, thanks to our build configuration, copy the module DLLs to the correct output directory (`build/Debug/modules/`).

3.  **Run the Client application:**
    ```sh
    dotnet run --project src/Client/Axorith.Client.csproj
    ```

## Roadmap

The project is in its early stages. The path forward is clear and focused on delivering value to power users.

*   **MVP (Minimum Viable Product)**
    *   [x] Stable Core architecture with module loading.
    *   [x] Functional Client with session creation and editing.
    *   [ ] Implement `Start/Stop` session logic in the UI.
    *   [ ] First-party modules: Site Blocker, Application Launcher.
    *   [ ] Basic API-based module: Spotify.

*   **Post-MVP**
    *   [ ] More modules (Telegram, Slack, VS Code integration).
    *   [ ] Session scheduling and automation.
    *   [ ] A proper module marketplace/browser in the app.

*   **Long-Term Vision**
    *   [ ] Cloud sync for presets.
    *   [ ] Mobile clients for remote session control.
    *   [ ] Deep work analytics and reporting.

## Contributing

We believe in the power of community. If you share our philosophy and want to contribute to the future of focused work, we welcome your input. Feel free to open an issue to report a bug or suggest a feature, or fork the repository and submit a pull request.

## License

Source-available software licensed under the Axorith Business Source License (BSL). See [LICENSE.md](LICENSE.md) for details.