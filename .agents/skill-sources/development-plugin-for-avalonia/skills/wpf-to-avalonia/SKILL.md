---
name: wpf-to-avalonia
description: Port WPF applications and patterns to Avalonia, including dependency-property mapping, bindings, resources, templates, layout, rendering, and shell behavior. Use for `DependencyProperty`, routed-command, trigger, `Frame` or `Page`, `OnRender`, `HwndHost`, or other WPF-to-Avalonia migration work.
---

# WPF to Avalonia

Start with:

- `../../references/64-wpf-to-avalonia-modern-ui-conversion-index.md`
- `../../references/wpf-to-avalonia/README.md`

Prioritize the chapters that match the WPF source:

- property system, bindings, resources, styles, and templates
- layout, navigation, windows, dialogs, and dispatcher workflows
- rendering, animation, popup, interop, and document workflows
- advanced control families, selection models, and platform services

## Workflow

1. Map the WPF concept first, then choose the Avalonia equivalent or redesign.
2. Call out where Avalonia does not preserve a WPF subsystem directly, especially triggers, navigation, and interop.
3. Replace resource, layout, and rendering assumptions before doing control-by-control cleanup.
4. Keep the migration narrative explicit about what stays familiar and what changes materially.

## Rules

- Do not promise WPF trigger, `CommandManager`, or `HwndHost` parity where it does not exist.
- Use Avalonia selectors, templates, and state patterns rather than recreating WPF internals.
- Keep document and printing workflows in a separate explicit decision track.
