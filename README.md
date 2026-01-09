<div align="center">
  <img src="docs/assets/github-banner.jpg" alt="Axorith Banner" width="100%">
  <p/>
  <p>
    <a href="https://discord.gg/axorith">
      <img src="https://img.shields.io/discord/1433475181447352414?label=Join%20Discord&logo=discord&style=for-the-badge&color=5865F2" alt="Discord">
    </a>
    <a href="https://github.com/axorithlabs/axorith/releases/latest">
      <img src="https://img.shields.io/github/downloads/axorithlabs/axorith/total?label=Downloads&style=for-the-badge&color=2ea44f" alt="Downloads">
    </a>
    <a href="https://github.com/axorithlabs/axorith/blob/main/LICENSE.md">
      <img src="https://img.shields.io/badge/License-BSL%201.1-blue?style=for-the-badge" alt="License">
    </a>
  </p>
</div>  

<h1 align="center">The Problem: The "Context Switching Tax"</h1>

Every time you start a task, you pay a tax in time and willpower. This 15-minute setup ritual is a barrier to entry. It's friction.

| The Old Way (Manual Chaos)            | The Axorith Way (Instant Context)       |
|:--------------------------------------|:----------------------------------------|
| 😩 Struggle to start working.         | ✅ **Deep Work.**          |
| 🎮 Can't fully disconnect after work. | ✅ **Gaming Mode.**        |
| 🎵 Fiddling with apps & smart home.   | ✅ **Lights & Media adjust instantly.** |
| 🛡️ Manual distraction blocking.       | ✅ **Automatic Distraction Blocker.**   |
| **15 minutes of friction.**           | **< 15 seconds to your flow state.**    |

<h1 align="center">The Philosophy: Your Mind is the Kernel</h1>

Axorith was born from a simple, powerful observation: **the modern digital workspace is fundamentally broken.**

The very tools meant to help us have become the primary source of friction. Existing applications only treat the symptoms — they are features *within* the chaos.

**Axorith is not another app. It's a remote control for your digital life.**
We believe you shouldn't spend mental energy setting up your environment. Whether you are coding, gaming, or winding down for the night, Axorith automates your apps, your home, and your focus.

This philosophy is built on three core principles, embodied in our key features:

---

### 1. ⚙️ You Are In Control, Not The Machine.

It's not about complex settings, but about meaningful control. You define the rules for your focus, codifying your entire workflow for different tasks into reusable presets.

> **Core Feature: Session Presets**
> Design your ideal environment for "Work" or "Play." Axorith launches any application, arranges your windows, starts your media, and enables your distraction blocker.

> **Core Feature: Session Scheduler**
> True autopilot. Schedule your "Deep Work" session to start at 9:00 AM and auto-switch to "Rest" at 10:00 AM. Your PC and room adapt instantly without you touching a thing.

### 2. 🧩 Radical Modularity, Not A Locked Cage.

Your workflow is unique. We don't lock you into our way of thinking. The entire system is built on plugins. Axorith provides the foundation; you choose the instruments.

> **Core Feature: A True Plugin Ecosystem**
> The entire system is built on a powerful SDK that lets you and the community integrate any tool with an API. A clean, well-documented, developer-first approach makes creating and sharing your own modules simple.

### 3. 🛡️ Unmatched Stability, Not Constant Fear.

Your focus is fragile. The tools that protect it must be bulletproof. We built Axorith to be the most reliable part of your workflow.

> **Core Feature: Client-Server Architecture**
> The UI (`Client`) is completely separate from the engine (`Host`). If the user interface crashes for any reason, your schedule and blockers **keep running** without interruption. Simply restart the UI and reconnect.

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

<h1 align="center">Roadmap & Development</h1>

Axorith is under active development, moving towards a powerful, stable release. Our vision is ambitious, and our progress is transparent.

*   **Milestone 1: The Foundation**
    *   [x] Bulletproof client-server architecture for maximum stability.
    *   [x] A powerful, reactive SDK for module development.
    *   [x] A core set of powerful modules (App/Site Blocker, Media Control, Universal Launchers).
    *   [x] Session scheduling.

*   **Milestone 2: The Ecosystem**
    *   [ ] Flawless onboarding and user experience.
    *   [ ] Cloud sync for presets.
    *   [ ] An in-app module browser and marketplace.
    *   [ ] MacOS & Linux support.

*   **Milestone 3: The "Digital Life System"**
    *   [ ] Deeper OS integrations, team features, and focus analytics.

For a detailed, up-to-the-minute view of our task board, bug reports, and current development status, visit our public YouTrack project.

[**➡️ View the Live Development Board on YouTrack**](https://axorithlabs.youtrack.cloud/agiles/192-1/current)

<h1 align="center">Join the Community</h1>

Have questions? Ideas? Want to see what's next? Join our community to chat with the developers and other users.

[**➡️ Join the Axorith Labs Discord Server**](https://discord.gg/axorith)

<h1 align="center">Contributing</h1>

We believe in the power of community. If you share our philosophy, we welcome your input. Please see our [Contributing Guidelines](CONTRIBUTING.md) to get started.

<h1 align="center">License & Monetization</h1>

Axorith is source-available under the Business Source License (BSL). We aim to build a sustainable open-source project. For details on what this means for you and how we plan to fund development, please read our [Monetization Philosophy](docs/monetization.md).

<h1 align="center">SAST Tools</h1>

[PVS-Studio](https://pvs-studio.com/en/pvs-studio/?utm_source=website&utm_medium=github&utm_campaign=open_source) - static analyzer for C, C++, C#, and Java code.