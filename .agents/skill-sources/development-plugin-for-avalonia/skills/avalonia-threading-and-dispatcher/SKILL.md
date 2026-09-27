---
name: avalonia-threading-and-dispatcher
description: Design or review Avalonia reactive flows, dispatcher usage, timers, async UI coordination, and thread-affinity boundaries. Use for `Dispatcher.UIThread`, background work handoff, timer selection, reactive pipeline fixes, or UI-thread correctness bugs.
---

# Avalonia Threading and Dispatcher

Start with:

- `../../references/03-reactive-threading.md`
- `../../references/47-dispatcher-priority-operations-and-timers.md`

Load these when quality hardening matters:

- `../../references/08-performance-checklist.md`
- `../../references/27-diagnostics-profiling-and-devtools.md`

## Workflow

1. Identify which state is allowed off-thread and which UI mutations must stay on the UI thread.
2. Pick the right dispatch primitive: immediate post, prioritized operation, or timer.
3. Keep reactive or async chains explicit about scheduler and dispatch boundaries.
4. Verify cancellation, teardown, and shutdown behavior for long-lived subscriptions or timers.

## Rules

- Never hide UI-thread assumptions in helper layers.
- Prefer explicit dispatcher boundaries over accidental thread capture.
- Use timers and background loops sparingly and always with disposal or cancellation.
