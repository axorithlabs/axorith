# Design System: Axorith

This guide is for Google Stitch mockups of Axorith's Avalonia desktop client. Axorith is a local focus-session orchestrator: users create presets, start committed sessions, and configure the apps, blockers, schedules, and actions attached to them. Design operational screens, not a marketing website.

## 1. Visual Theme & Atmosphere

A quiet, precise dark workspace with enough structure to make session state and consequential actions unmistakable. Keep the interface compact and tool-like, with restrained variation and almost no decorative motion.

| Dial | Level | Intent |
|---|---:|---|
| Creativity | 4 | Clear hierarchy with small character, no visual theatrics |
| Density | 7 | Fit real settings and session details without crowding |
| Variance | 4 | Consistent panels; vary layout only when it clarifies priority |
| Motion | 2 | Brief state transitions; no ambient animation |

## 2. Color Palette & Roles

- **Canvas** (`#050505`) — Main window background.
- **Rail Surface** (`#0A0A0A`) — Header, footer, and secondary regions.
- **Panel Surface** (`#111111`) — Preset tiles, forms, and dialogs.
- **Hover Surface** (`#161616`) — Hovered rows and controls.
- **Pressed Surface** (`#1A1A1A`) — Pressed controls and selected subtle fills.
- **Primary Text** (`#E0E0E0`) — Titles and main content.
- **Secondary Text** (`#B0B0B0`) — Supporting labels and descriptions.
- **Muted Text** (`#888888`) — Metadata and inactive details; keep readable at small sizes.
- **Disabled Text** (`#666666`) — Disabled controls only.
- **Structural Border** (`#222222`) — Quiet panel outlines and separators.
- **Primary Border** (`#333333`) — Standard control outlines and section dividers.
- **Hover Border** (`#555555`) — Interactive hover feedback.
- **Axorith Blue** (`#2563EB`) — The single accent for primary actions, selected states, and focus indicators. Hover `#3B82F6`; pressed `#1D4ED8`.
- **Success** (`#10B981`), **Caution** (`#E6C878`), and **Error** (`#FF4444`) — Semantic status colors only; never use them as extra brand accents.

Keep the palette neutral and nearly flat. Do not add purple, neon, gradients, pure-black surfaces, or a second decorative accent.

## 3. Typography Rules

- Use a clean sans-serif that fits the native Fluent desktop interface. In Stitch mockups, use Geist or a similar restrained sans-serif; do not make the design depend on a bundled font.
- Default body and control text: `13–14px`. Section titles: `16–20px`. Screen titles: `20–24px`. Session time may rise to `24px` and use tabular numerals.
- Use weight and spacing for hierarchy, not oversized display headlines. Keep descriptions short enough to scan; wrap long module names and paths instead of shrinking them.
- Use monospace only for code, paths, or technical identifiers when it improves scanning. Do not use serif typography in the product UI.

## 4. Component Stylings

- **Panels and preset tiles:** `#0A0A0A` or `#111111` fill, 1px border, `8px` radius. Use a brighter border or surface to show selection. Avoid large shadows and oversized rounded cards.
- **Buttons:** `6px` radius with a clear label. Blue fill for the main action, outlined/quiet treatment for secondary actions, red treatment for destructive actions. Show hover, pressed, disabled, and keyboard-focus states. Keep important actions visible without hover.
- **Inputs:** Label above the field; helper text below; validation directly beside the affected field. Use the blue focus ring and preserve readable contrast.
- **Session status:** State, workspace name, remaining time, and relevant protections should read as one ordered summary. Use color and text together; never communicate state by color alone.
- **Dialogs:** Use for start review and emergency unlock. State the consequence before the action; make cancel or close behavior easy to find. Give hold-to-confirm controls a visible progress state.
- **Loading and empty states:** Keep them specific to the task, such as “Scanning for installed apps…” or a concise prompt to create a preset. Use a progress indicator only when work is active; do not add decorative spinners or invented metrics.
- **Icons:** Use simple, consistent line or filled system-style icons. Pair unfamiliar icons with visible labels or tooltips.

## 5. Layout Principles

- Treat the client as a resizable desktop window. Use the existing `1600×900` window as the roomy reference and also compose for a narrower `1100×720` window.
- On the dashboard, keep session status at the top, a wrapping collection of real preset tiles in the work area, and the main session actions in a stable bottom bar. An empty state may be centered in the work area.
- Preset tiles are real repeated data objects, so a regular grid is appropriate. Do not turn product screens into a row of generic marketing feature cards.
- Keep the session editor in a readable vertical sequence: commitment and end behavior, **Start On**, modules, **Stop On**, then follow-up behavior. Keep save and cancel actions easy to locate.
- Use consistent `8px` corner radii and measured spacing. Separate nested sections with spacing or a divider instead of stacking cards inside cards.
- Preserve the native title bar and desktop interaction cues. Do not add a web navbar, hero image, giant headline, or mobile hamburger navigation.

## 6. Responsive Behavior & Accessibility

- Reflow when the window narrows: preset tiles reduce columns and then use one column; status details wrap; editor fields stack. Keep every action available and avoid horizontal scrolling.
- Maintain keyboard navigation, visible focus, clear selection, and labels for icon-only controls. Use pointer-sized controls without making the desktop layout feel oversized.
- Keep errors, warnings, countdowns, and active-session state readable at normal scaling. Do not rely on subtle color differences or hover-only discovery.

## 7. Motion & Interaction

- Use short `120–200ms` transitions for border, surface, and selection changes. Keep movement small and direct; no spring choreography or staggered list entrances.
- Update timers and status text in place. Avoid pulsing, floating, shimmer loops, animated backgrounds, and layout movement.
- Respect reduced-motion preferences and the native platform's focus and pressed feedback.

## 8. Screen Patterns

- **Dashboard:** Distinguish idle, active, break, and blocked/attention states. Show the next useful action prominently. Keep stop and emergency unlock actions visually distinct and explain their consequences.
- **Preset editor:** Organize configuration by when a session starts, what it runs, when it stops, and what happens next. Show validation errors where they can be fixed.
- **Setup wizard:** Show scan progress, selectable preset categories, selected state, and a clear create/cancel footer.
- **Settings:** Group related toggles and explain their effect in one short supporting line.

Use realistic labels such as “Deep Work”, “Coding”, or “Gaming”. Show actual modules and meaningful times; do not invent percentages, cloud status, or analytics.

## 9. Anti-Patterns (Banned)

- No emojis, AI copywriting clichés, fake metrics, placeholder brands, or filler instructions.
- No purple/neon palette, glowing controls, excessive gradients, pure-black fills, or decorative color accents.
- No marketing hero, inline photos in headings, oversized editorial typography, or generic equal-card feature rows.
- No hiding primary, destructive, or safety actions until hover; no ambiguous emergency-unlock affordance.
- No card-inside-card nesting, tiny low-contrast metadata, unexplained icons, or color-only status communication.
- No perpetual motion, generic loading spinner, invented cloud sync, or decorative dashboards unrelated to focus sessions.
