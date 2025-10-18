# Axorith Architecture

This document outlines the high-level architecture of the Axorith project. It serves as a guide for developers and contributors to understand the core principles, components, and their interactions.

## Core Philosophy

The architecture is designed around three core principles:

1.  **Modularity:** The system must be extensible. All functionality is implemented as isolated modules (plugins). The Core engine knows nothing about specific features, only about the general contract of a module.
2.  **Decoupling:** Components must be loosely coupled. The UI (`Client`) is completely separate from the business logic (`Core`). Modules are separate from the Core. This is achieved through a shared `Sdk` that defines interfaces (contracts).
3.  **Testability:** Every piece of the system, especially the Core, must be testable in isolation. This is achieved through Dependency Injection (DI) and an interface-first design.

## Component Diagram

The project is divided into several key components with clear dependencies. An arrow `A -> B` means `A` depends on `B`.

```
+-----------+      +-----------+
|  Client   |----->|   Core    |
+-----------+      +-----------+
      |                  |
      |   +-----------+  |
      +-->|   Shared  |<-+
      |   +-----------+  |
      |         ^        |
      v         |        v
+-----------+   |   +-----------+
|  Modules  |---+-->|    Sdk    |
+-----------+       +-----------+
```

## Component Breakdown

### 1. `Axorith.Sdk` (The Law)

*   **Responsibility:** Defines the public contracts for the entire ecosystem. This is the "law" that all other components must follow.
*   **Contents:** Interfaces (`IModule`, `IModuleContext`), enums (`Platform`, `SettingType`), and data models (`ModuleSetting`).
*   **Key Principles:** Contains **zero** implementation logic. Has **zero** external dependencies besides the base .NET library. It is the foundation of the entire project.

### 2. `Axorith.Core` (The Engine)

*   **Responsibility:** The headless, no-UI "brain" of the application. It manages the entire lifecycle of modules and sessions.
*   **Contents:**
    *   **Services:** `ModuleLoader`, `ModuleRegistry`, `PresetManager`, `SessionManager`.
    *   **Models:** `SessionPreset`, `ConfiguredModule`.
    *   **DI Setup:** The `AxorithHost` class, which configures and exposes all services.
*   **Key Principles:** Built entirely on Dependency Injection. Services are registered as interfaces and depend only on other interfaces, not concrete implementations. The Core knows nothing about the `Client`.

### 3. `Axorith.Client` (The Control Panel)

*   **Responsibility:** The user-facing application. It provides a graphical interface for the user to interact with the `Core`.
*   **Contents:** Avalonia Views (`.axaml`), ViewModels, and Converters.
*   **Key Principles:** Built on the **MVVM (Model-View-ViewModel)** pattern.
    *   **Views** are "dumb" and only define the layout.
    *   **ViewModels** contain all UI logic and communicate with the `Core` through the `AxorithHost` facade.
    *   The `Client` depends on the `Core`, but the `Core` does not depend on the `Client`.

### 4. `Axorith.Modules` (The Tools)

*   **Responsibility:** Implementations of specific features. Each module is a separate project that implements the `IModule` interface from the `Sdk`.
*   **Contents:** A collection of projects like `Axorith.Module.Test`, `Axorith.Module.Spotify`, etc.
*   **Key Principles:** A module is a self-contained unit. It depends only on the `Sdk` and `Shared` libraries. It must not, under any circumstances, depend on the `Core` or other modules directly.

### 5. `Axorith.Shared` (The Toolbox)

*   **Responsibility:** Provides common, reusable code needed by multiple, otherwise independent parts of the system (e.g., `Core` and `Modules`).
*   **Contents:** A collection of small, focused libraries like `Axorith.Shared.Utils` and `Axorith.Shared.Exceptions`.
*   **Key Principles:** A project in `Shared` should have a single, clear purpose. This avoids creating a monolithic "common" library.
```