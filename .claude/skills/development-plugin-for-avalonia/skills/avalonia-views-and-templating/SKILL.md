---
name: avalonia-views-and-templating
description: Build or refactor Avalonia view composition, view-location strategies, templates, templated content, and tree-inspection patterns. Use for view locator work, `DataTemplate` or `IDataTemplate` selection, templated parent issues, logical or visual tree traversal, or content-template architecture.
---

# Avalonia Views and Templating

Start with:

- `../../references/11-user-views-locator-and-tree-patterns.md`
- `../../references/38-data-templates-and-idatatemplate-selector-patterns.md`
- `../../references/51-template-content-and-func-template-patterns.md`

Load these when lookup or debugging matters:

- `../../references/39-visual-tree-inspection-and-traversal.md`
- `../../references/40-logical-tree-inspection-and-traversal.md`

## Workflow

1. Choose whether composition belongs in views, templates, or view-location infrastructure.
2. Keep template selection deterministic and typed when possible.
3. Use logical or visual tree traversal deliberately and only where ownership is clear.
4. Separate reusable templates from app-specific shell composition.

## Rules

- Prefer simple `DataTemplate` and `x:DataType` flows before custom selector infrastructure.
- Keep tree walking out of hot paths when a direct reference or binding can do the job.
- Make template-part and templated-parent assumptions explicit.
