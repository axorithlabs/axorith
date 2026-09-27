---
name: winforms-to-avalonia
description: Port WinForms applications and patterns to Avalonia, including layout, control migration, owner-draw replacement, application lifetime, resources, and workflow shells. Use for `Control` or `Form` migration, `DataGridView` and `ListView` replacement, `OnPaint` ports, dialog or tray flows, or WinForms-to-Avalonia modernization work.
---

# WinForms to Avalonia

Start with:

- `../../references/63-winforms-to-avalonia-modern-ui-conversion-index.md`
- `../../references/winforms-to-avalonia/README.md`

Prioritize the detailed chapters that match the source app:

- lifecycle, docking, layout, and navigation shells
- data-binding, validation, list or tree controls
- dialogs, tray, menus, keyboard processing, and platform services
- owner-draw, custom controls, rendering, and advanced workflows

## Workflow

1. Identify the WinForms source idiom precisely before proposing an Avalonia replacement.
2. Replace layout and painting assumptions first; they drive most downstream changes.
3. Map stateful UI workflows to Avalonia controls and templating instead of recreating WinForms plumbing.
4. End with a modernization plan, not only a one-to-one API substitution list.

## Rules

- Do not emulate WinForms layout or paint cycles inside Avalonia.
- Prefer Avalonia templating and composition over direct control painting patterns.
- Keep lifetime, threading, and platform-service changes explicit in the migration plan.
