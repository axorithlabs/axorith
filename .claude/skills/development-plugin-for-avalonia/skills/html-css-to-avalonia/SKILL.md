---
name: html-css-to-avalonia
description: Port HTML or CSS mental models, layouts, controls, theming, and modern web UI patterns to Avalonia. Use for website-to-desktop translation, CSS-to-Avalonia styling work, responsive layout conversion, custom-element or Shadow DOM mapping, or web-style design-system migration into Avalonia.
---

# HTML/CSS to Avalonia

Start with:

- `../../references/62-html-css-to-avalonia-modern-ui-conversion-index.md`
- `../../references/html-to-avalonia/README.md`

Use the detailed lane docs only for the parts the request actually touches:

- layout, responsive sizing, and positioning
- cascade, selectors, tokens, and theme architecture
- forms, navigation, overlays, lists, and command surfaces
- advanced UI patterns such as tabs, split panes, pull-to-refresh, or custom elements

## Workflow

1. Translate the web mental model first: layout, state, semantics, and styling architecture.
2. Map CSS constructs to Avalonia resources, selectors, templates, and control choices explicitly.
3. Keep responsive and input behavior intentional instead of copying browser assumptions blindly.
4. Validate accessibility, focus, and reduced-motion equivalents after the port.

## Rules

- Do not treat Avalonia like a browser with different syntax.
- Convert CSS architecture, not only individual properties.
- Keep HTML semantics and Avalonia control contracts aligned in the explanation.
