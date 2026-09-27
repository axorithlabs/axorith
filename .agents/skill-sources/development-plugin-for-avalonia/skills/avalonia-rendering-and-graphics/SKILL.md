---
name: avalonia-rendering-and-graphics
description: Implement or review Avalonia animations, compositor usage, custom drawing, Skia rendering, geometry, hit testing, and advanced rendering interop. Use for transitions, frame-loop work, `Render` overrides, custom visuals, drawing optimizations, or OpenGL or Vulkan hosting boundaries.
---

# Avalonia Rendering and Graphics

Start with:

- `../../references/12-animations-transitions-and-frame-loops.md`
- `../../references/14-custom-drawing-text-shapes-and-skia.md`
- `../../references/15-compositor-and-custom-visuals.md`

Load these as needed:

- `../../references/37-shapes-geometry-and-hit-testing.md`
- `../../references/59-media-colors-brushes-and-formatted-text-practical-usage.md`
- `../../references/61-rendering-and-interop-boundaries-opengl-vulkan-framebuffer.md`

## Workflow

1. Prefer built-in animations and transitions before custom frame-loop logic.
2. Use compositor and custom visuals only when there is a concrete interaction or performance reason.
3. Keep custom drawing and geometry ownership explicit.
4. Treat rendering interop as a boundary decision, not a casual extension point.

## Rules

- Keep `Render` overrides small and predictable.
- Separate scene description, animation timing, and platform interop concerns.
- Use advanced interop only when the built-in rendering stack is insufficient.
