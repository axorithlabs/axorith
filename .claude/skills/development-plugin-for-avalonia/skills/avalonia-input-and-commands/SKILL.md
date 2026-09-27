---
name: avalonia-input-and-commands
description: Implement or debug Avalonia routed input, focus management, keyboard navigation, commands, gestures, drag/drop workflows, text editing behavior, and interaction routing. Use for `ICommand`, `KeyBinding`, routed events, focus bugs, drag/drop, scroll interaction, or text input edge cases.
---

# Avalonia Input and Commands

Start with:

- `../../references/18-input-system-and-routed-events.md`
- `../../references/19-focus-and-keyboard-navigation.md`
- `../../references/24-commands-hotkeys-and-gestures.md`

Load these when the interaction surface needs them:

- `../../references/34-dragdrop-workflows.md`
- `../../references/57-scrollviewer-offset-anchoring-and-snap-points.md`
- `../../references/58-textbox-editing-clipboard-undo-and-input-options.md`
- `../../references/36-adorners-focus-and-overlay-layers.md`

## Workflow

1. Decide whether the interaction belongs in routed events, commands, or control state.
2. Keep focus order, key handling, and command routing explicit.
3. Model drag/drop and text-editing behavior as part of the interaction contract, not as incidental details.
4. Validate pointer, keyboard, and programmatic interaction paths separately.

## Rules

- Prefer command and state dispatch over large event-handler trees.
- Keep access-key, focus, and keyboard-navigation rules testable.
- Treat text editing and drag/drop as behavior with edge cases, not cosmetic features.
