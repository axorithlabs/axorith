---
name: avalonia-bootstrap-and-lifetime
description: Design or troubleshoot Avalonia startup wiring, `AppBuilder` configuration, application lifetimes, platform bootstrap, XAML compiler setup, and AOT-friendly build configuration. Use for desktop or single-view startup, `App.axaml` / `App.xaml.cs` composition, platform entrypoints, trimming or NativeAOT readiness, or startup refactors.
---

# Avalonia Bootstrap and Lifetime

Start with:

- `../../references/00-api-map.md`
- `../../references/01-architecture-and-lifetimes.md`
- `../../references/05-platforms-and-bootstrap.md`
- `../../references/06-msbuild-aot-and-tooling.md`
- `../../references/41-xaml-compiler-and-build-pipeline.md`

Load these when the request touches runtime services or platform shell integration:

- `../../references/48-toplevel-window-and-runtime-services.md`
- `../../references/29-storage-provider-and-file-pickers.md`

## Workflow

1. Choose the lifetime model first: desktop, single-view, or an activatable lifetime hook.
2. Lock platform bootstrap early and keep platform options on `AppBuilder.With<T>(...)`.
3. Confirm the XAML compiler, compiled-binding, trimming, and AOT setup before UI work grows.
4. Keep startup wiring thin: app construction, service registration, and root-window or root-view composition only.

## Rules

- Keep defaults pinned to Avalonia `11.3.12`.
- Prefer compiled XAML and compiled bindings for production startup paths.
- Keep platform-specific code isolated to entrypoints and platform option configuration.
- Treat reflection-heavy startup helpers as opt-in tradeoffs and call them out explicitly.
