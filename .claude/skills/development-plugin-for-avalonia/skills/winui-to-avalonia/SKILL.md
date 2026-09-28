---
name: winui-to-avalonia
description: Port WinUI or Windows App SDK applications to Avalonia, including shell design, dependency-property patterns, theme resources, composition, commands, dialogs, and platform integration. Use for `NavigationView`, `ContentDialog`, `ThemeResource`, `VisualStateManager`, `ItemsRepeater`, `AppWindow`, or broader WinUI-to-Avalonia migration work.
---

# WinUI to Avalonia

Start with:

- `../../references/65-winui-to-avalonia-modern-ui-conversion-index.md`
- `../../references/winui-to-avalonia/README.md`

Prioritize the chapters that match the source app:

- object or property system, bindings, resources, and state mapping
- `NavigationView`, dialog, titlebar, and multi-window shell behavior
- composition, rendering, scroll, gesture, and advanced control migration
- platform services, activation, storage, notifications, and WebView boundaries

## Workflow

1. Identify the WinUI shell and state model before moving individual controls.
2. Translate `ThemeResource`, `VisualStateManager`, and composition assumptions explicitly.
3. Rebuild platform integration through Avalonia services rather than Windows-only primitives.
4. Keep modern-shell decisions intentional instead of chasing one-to-one surface parity.

## Rules

- Do not treat `NavigationView`, `ContentDialog`, or `AppWindow` as direct API renames.
- Separate composition effects from essential interaction behavior.
- Make Windows-specific contracts and Avalonia cross-platform replacements explicit.
