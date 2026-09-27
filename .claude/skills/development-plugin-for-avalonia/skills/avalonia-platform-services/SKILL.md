---
name: avalonia-platform-services
description: Integrate or troubleshoot Avalonia platform services such as storage provider, clipboard, launcher, drag/drop, screens, and top-level runtime helpers. Use for file pickers, external-open flows, clipboard or data-transfer bugs, multi-screen behavior, or platform service abstractions.
---

# Avalonia Platform Services

Start with:

- `../../references/29-storage-provider-and-file-pickers.md`
- `../../references/31-clipboard-and-data-transfer.md`
- `../../references/32-launcher-and-external-open.md`
- `../../references/33-screens-and-display-awareness.md`

Load these when workflow overlap exists:

- `../../references/34-dragdrop-workflows.md`
- `../../references/48-toplevel-window-and-runtime-services.md`

## Workflow

1. Resolve which top-level or window owns the service interaction.
2. Keep platform-service abstractions thin and centered on Avalonia APIs.
3. Model data-transfer contracts explicitly for drag/drop and clipboard flows.
4. Validate multi-window, multi-screen, and external-open behavior on the target platform.

## Rules

- Prefer Avalonia service abstractions over platform-specific escape hatches.
- Keep file-picker and launcher flows async and user-driven.
- Treat clipboard and drag/drop formats as part of the app contract.
