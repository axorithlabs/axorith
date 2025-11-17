<div align="center">
  <h1>Axorith</h1>
  <h3>The Productivity OS</h3>
  <p>
    <strong>One click to automate your focus rituals. Designed with a Deep Work philosophy.</strong>
  </p>
</div>

<!--
<p align="center">
  <img src="https://axorith.com/demo.gif" alt="Axorith Demo">
</p>
-->

## The Problem: The "Focus Tax"

Every time you start a task, you pay a tax in time and willpower. This 15-minute setup ritual is a barrier to entry. It's friction.

| The Old Way (Manual Chaos)            | The Axorith Way (Automated Flow)        |
|:--------------------------------------|:----------------------------------------|
| 😩 Hunt for apps & project files.     | ✅ **Apps launch instantly.**            |
| 🎵 Find the right focus playlist.     | ✅ **Music starts automatically.**       |
| 🔇 Silence a storm of notifications.  | ✅ **Distractions are muted.**           |
| 🛡️ Manually enable website blockers. | ✅ **Focus shield is engaged.**          |
| 🖥️ Arrange windows across monitors.  | ✅ **Workspace is ready.**               |
| **15 minutes of friction.**           | **Less than 15 seconds to flow state.** |

## The Philosophy: Your Mind is the Kernel

Axorith was born from a simple, powerful observation: **the modern digital workspace is fundamentally broken.**

The very tools meant to help us have become the primary source of friction. Existing applications only treat the symptoms — they are features *within* the chaos.

**Axorith is not another application. It is an opinionated operating environment.** We believe your computer should be a silent, purpose-built instrument for your mind, not a battlefield for your attention.

This philosophy is built on three core principles, embodied in our key features:

---

### 1. ⚙️ You Are In Control, Not The Machine.

It's not about complex settings, but about meaningful control. You define the rules for your focus, codifying your entire workflow for different tasks into reusable, one-click launchers.

> **Core Feature: Session Presets**
> Design your ideal environment for "Coding," "Writing," or "Gaming." One click launches your apps, sets up your windows, starts your music, and engages your focus shield.

### 2. 🧩 Radical Modularity, Not A Locked Cage.

Your workflow is unique. We don't lock you into our way of thinking. The entire system is built on plugins. Axorith provides the conductor's podium; you choose the instruments.

> **Core Feature: A True Plugin Ecosystem**
> The entire system is built on a powerful SDK that lets you and the community integrate any tool with an API. A clean, well-documented, developer-first approach makes creating and sharing your own modules simple.

### 3. 🛡️ Unmatched Stability, Not Constant Fear.

Your focus is fragile. The tools that protect it must be bulletproof. We built Axorith to be the most reliable part of your workflow.

> **Core Feature: Client-Server Architecture**
> The UI (`Client`) is completely separate from the engine (`Host`). If the user interface crashes for any reason, your focus session **keeps running** without interruption. Simply restart the UI and reconnect.

---

<details>
  <summary><strong>Peek Under the Hood: Tech Stack & Architecture</strong></summary>

### Tech Stack
*   **.NET 10** & **C# 14**
*   **Avalonia UI** for a true cross-platform native UI on Windows, macOS, and Linux.
*   **ReactiveUI (MVVM)** for a modern, reactive UI architecture.
*   **Serilog** for structured, production-ready logging.

### Architecture
Axorith is built on a clean, modular architecture to ensure stability, testability, and extensibility. You can read the full guide [here](docs/architecture.md).
</details>

---

## Roadmap & Development

Axorith is under active development, moving towards a powerful, stable release. Our vision is ambitious, and our progress is transparent.

*   **Milestone 1: The Foundation (In Progress)**
    *   Bulletproof client-server architecture for maximum stability.
    *   A powerful, reactive SDK for module development.
    *   A core set of powerful modules (App/Site Blocker, Spotify, IDE Launchers).
    *   Secure authentication provider system.

*   **Milestone 2: The Ecosystem**
    *   Flawless onboarding and user experience.
    *   Session scheduling and cloud sync for presets.
    *   An in-app module browser and marketplace.

*   **Milestone 3: The "Productivity OS"**
    *   Deeper OS integrations, team features, and focus analytics.

For a detailed, up-to-the-minute view of our task board, bug reports, and current development status, visit our public YouTrack project.

[**➡️ View the Live Development Board on YouTrack**](https://axorithlabs.youtrack.cloud/agiles/192-1/current)

## Join the Community

Have questions? Ideas? Want to see what's next? Join our community to chat with the developers and other users.

[**➡️ Join the Axorith Labs Discord Server**](https://discord.gg/bEmxUzj6ta)

## Contributing

We believe in the power of community. If you share our philosophy, we welcome your input. Please see our [Contributing Guidelines](CONTRIBUTING.md) to get started.

## License & Monetization

Axorith is source-available under the Business Source License (BSL). We aim to build a sustainable open-source project. For details on what this means for you and how we plan to fund development, please read our [Monetization Philosophy](docs/monetization.md).

## SAST Tools

[PVS-Studio](https://pvs-studio.com/en/pvs-studio/?utm_source=website&utm_medium=github&utm_campaign=open_source) - static analyzer for C, C++, C#, and Java code.
