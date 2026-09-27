---
name: avalonia-testing-diagnostics-and-performance
description: Validate Avalonia applications with headless tests, render or UI tests, diagnostics tooling, troubleshooting workflows, and performance reviews. Use for test strategy, DevTools or profiler usage, performance regressions, rendering investigations, or production hardening passes.
---

# Avalonia Testing, Diagnostics, and Performance

Start with:

- `../../references/26-testing-stack-headless-render-and-ui-tests.md`
- `../../references/27-diagnostics-profiling-and-devtools.md`
- `../../references/08-performance-checklist.md`
- `../../references/07-troubleshooting.md`

Load this when an integrated sample helps:

- `../../references/09-end-to-end-examples.md`

## Workflow

1. Pick the right confidence level: unit, headless render, UI, or manual diagnostics.
2. Reproduce the issue before optimizing or broadening the fix.
3. Use diagnostics output to narrow the subsystem before rewriting code.
4. End with a concrete regression-safety plan.

## Rules

- Measure first when performance is the claimed problem.
- Keep troubleshooting steps tied to reproducible symptoms.
- Add tests for bug classes, not only the single observed path.
