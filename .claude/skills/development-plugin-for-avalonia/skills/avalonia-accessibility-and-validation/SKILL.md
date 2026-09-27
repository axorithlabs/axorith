---
name: avalonia-accessibility-and-validation
description: Design or review Avalonia validation, data-error presentation, accessibility semantics, automation properties, focus order, and testable UI metadata. Use for validation pipelines, screen-reader support, keyboard-only UX, automation-tree issues, or accessibility hardening.
---

# Avalonia Accessibility and Validation

Start with:

- `../../references/22-validation-pipeline-and-data-errors.md`
- `../../references/23-accessibility-and-automation.md`
- `../../references/60-automation-properties-and-attached-behavior-patterns.md`

Load these when keyboard behavior is involved:

- `../../references/19-focus-and-keyboard-navigation.md`

## Workflow

1. Define how validation state is surfaced in the viewmodel, control, and visual layer.
2. Verify labels, focus order, and automation metadata together instead of as isolated fixes.
3. Keep automation names, help text, and control relationships stable.
4. Make the final accessibility contract easy to test.

## Rules

- Prefer `INotifyDataErrorInfo`-friendly flows for rich validation.
- Do not treat automation metadata as optional polish.
- Keep keyboard-only navigation credible before declaring a surface accessible.
