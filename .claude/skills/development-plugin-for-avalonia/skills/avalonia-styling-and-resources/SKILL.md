---
name: avalonia-styling-and-resources
description: Build or troubleshoot Avalonia styles, themes, resources, property-system usage, theme variants, resource packaging, and custom theme architecture. Use for selector fixes, attached properties, resource lookup problems, theme switching, library resource packaging, or code-only theme work.
---

# Avalonia Styling and Resources

Start with:

- `../../references/04-styles-themes-resources.md`
- `../../references/16-property-system-attached-properties-behaviors-and-style-properties.md`
- `../../references/17-resources-assets-theme-variants-and-xmlns.md`

Load these when needed:

- `../../references/28-custom-themes-xaml-and-code-only.md`
- `../../references/43-xaml-in-libraries-and-resource-packaging.md`
- `../../references/35-path-icons-and-vector-geometry-assets.md`

## Workflow

1. Decide which behavior belongs in properties, styles, templates, or resources.
2. Keep selectors and theme-variant boundaries predictable.
3. Package shared resources cleanly when the code lives in libraries.
4. Make lookup order and override points explicit before adding more layers.

## Rules

- Keep property metadata and style ownership clear.
- Prefer stable resource keys and predictable override layers over ad-hoc duplication.
- Use code-only themes only when XAML does not fit the requirement.
