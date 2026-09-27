---
name: avalonia-12-migration
description: Plan and execute migration from Avalonia 11.3.12 to Avalonia 12 using the repository's dedicated migration lane. Use for breaking-change review, source-backed API delta lookup, migration sequencing, and upgrades that should stay grounded in the current published Avalonia 12 documentation and generated artifacts.
---

# Avalonia 12 Migration

Start with:

- `../../references/68-avalonia-12-migration-guide.md`
- `../../references/69-avalonia-12-breaking-changes-and-new-api-catalog.md`
- `../../references/api-index-12.0.0-rc1-generated.md`

Use the stable lane for current implementation guidance unless the request explicitly targets this migration.

## Workflow

1. Confirm the source app is on Avalonia `11.3.12` or note the actual starting point.
2. Read the curated migration guide before touching code so sequencing is correct.
3. Use the breaking-change catalog for exhaustive impact review and the generated Avalonia 12 index for signature lookup.
4. Call out current published Avalonia 12 status explicitly when making recommendations.

## Rules

- Keep migration guidance grounded in the current published `12.0.0*` lane tracked by this repo.
- Do not let Avalonia 12 advice leak back into the default 11.3.12 skills.
- Separate required break fixes from optional modernizations.
