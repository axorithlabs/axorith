---
name: avalonia-layout-and-virtualization
description: Design or optimize Avalonia panels, measure and arrange behavior, custom layout controls, item virtualization, recycling, and large-data presentation. Use for `MeasureOverride` or `ArrangeOverride` work, invalidation bugs, virtualized item surfaces, or custom panel authoring.
---

# Avalonia Layout and Virtualization

Start with:

- `../../references/30-layout-measure-arrange-and-custom-layout-controls.md`
- `../../references/21-custom-layout-authoring.md`
- `../../references/20-itemscontrol-virtualization-and-recycling.md`

Load these when scrolling or templates are involved:

- `../../references/57-scrollviewer-offset-anchoring-and-snap-points.md`
- `../../references/38-data-templates-and-idatatemplate-selector-patterns.md`

## Workflow

1. Define layout contract first: desired size, final arrangement, invalidation rules.
2. Use built-in panels and recycling paths before creating a custom panel.
3. Verify how virtualization, scrolling, and templates interact on large data sets.
4. Measure before and after changes when performance is the reason for the work.

## Rules

- Use `InvalidateMeasure` and `InvalidateArrange` deliberately.
- Avoid per-item heavy logic inside measuring and arranging hot paths.
- Keep virtualization-friendly templates light and deterministic.
