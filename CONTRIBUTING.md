# Contributing to Axorith

First off, thank you for considering contributing. It’s people like you that make open-source projects and ambitious visions a reality.

We are building Axorith to solve a fundamental problem with the modern digital workspace. If you share our philosophy (as outlined in our [README.md](README.md)), we welcome your help. Every contribution, from a typo fix to a new module, is valuable.

This document provides a set of guidelines for contributing to Axorith.

## Code of Conduct

This project and everyone participating in it is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior.

## How Can I Contribute?

There are many ways to contribute, and not all of them involve writing code.

*   **Reporting Bugs:** If you find a bug, please open an issue. A well-documented bug report is a massive help.
*   **Suggesting Enhancements:** Have an idea for a new feature or a better way to do something? Open an issue to start a discussion.
*   **Writing Documentation:** Our `docs/` folder is the source of truth. Improving it helps everyone.
*   **Submitting Pull Requests:** If you're ready to write some code, we'd love to see it.

## Your First Contribution

Unsure where to begin? Look for issues tagged `good first issue`. These are tasks that are well-defined and a great way to get familiar with the codebase.

### Reporting Bugs

Before submitting a bug report, please search the existing issues to see if it has already been reported.

When creating a bug report, please include:
1.  **A clear, descriptive title.**
2.  **Steps to reproduce the bug.** Be as specific as possible.
3.  **What you expected to happen.**
4.  **What actually happened.** Include screenshots and log file excerpts if possible.
5.  **Your environment:** Operating System, .NET version.

### Suggesting Enhancements

We use GitHub issues to track feature ideas. Before creating a new one, please check if a similar idea has already been discussed.

When suggesting a feature, explain **why** it's needed and **what problem it solves**. A clear use-case is more valuable than a vague idea.

## Submitting a Pull Request (PR)

This is the core process for contributing code.

1.  **Fork the repository** and clone it locally.
2.  **Create a new branch** from `main`. Please use a descriptive name, like `feature/spotify-auth-refresh` or `fix/session-editor-crash`.
3.  **Make your changes.** Adhere to the existing code style and our architectural principles (see below).
4.  **Add tests.** If you're adding new functionality, it needs to be tested. If you're fixing a bug, add a test that proves the bug is fixed.
5.  **Update documentation.** If your changes affect the architecture or user-facing features, update the relevant `.md` files in the `docs/` folder.
6.  **Use meaningful commit messages.** We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification. (e.g., `feat: Add validation for Spotify API token`, `fix: Prevent crash when module DLL is corrupt`).
7.  **Push your branch** to your fork and **open a Pull Request** against the `main` branch of `axorithlabs/axorith`.
8.  **Write a clear PR description.** Explain what your PR does and link to the issue it resolves (e.g., "Closes #42").

## Our Architectural Principles (The Golden Rules)

To maintain the quality and integrity of the project, all contributions must respect these core principles.

*   **The SDK is Law.** The `Axorith.Sdk` project is the sacred contract. It should only contain interfaces, enums, and immutable models. Never add implementation logic to the SDK.
*   **The Core is Headless.** The `Axorith.Core` project must remain completely UI-agnostic. It should never reference `Avalonia` or any UI-related concepts. It's a pure engine.
*   **Modules are Isolated.** A module must only depend on the `Sdk` and `Shared` libraries. It cannot know about the `Core`, the `Client`, or other modules.
*   **The Client is "Dumb".** The `Axorith.Client` project should contain as little business logic as possible. Its job is to display data from ViewModels and forward user commands to the `Core`. All heavy lifting happens in the `Core`.
*   **Dependency Injection is Mandatory.** All services in the `Core` and ViewModels in the `Client` should receive their dependencies through constructors (preferably primary constructors). Do not use `new` to create services.

Pull requests that violate these principles will not be merged.

---

Thank you for your interest in building the future of focused work.