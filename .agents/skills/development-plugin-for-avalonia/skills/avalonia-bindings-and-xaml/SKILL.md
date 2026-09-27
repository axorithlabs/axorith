---
name: avalonia-bindings-and-xaml
description: Implement or troubleshoot Avalonia compiled bindings, runtime XAML loading, converters, markup extensions, binding notifications, dynamic resources, and AOT-safe XAML patterns. Use for `x:DataType`, `CompiledBinding`, converter wiring, `AvaloniaRuntimeXamlLoader`, resource lookup issues, or runtime markup refactors.
---

# Avalonia Bindings and XAML

Start with:

- `../../references/02-bindings-xaml-aot.md`
- `../../references/41-xaml-compiler-and-build-pipeline.md`
- `../../references/45-value-converters-single-multi-and-binding-wiring.md`
- `../../references/46-binding-value-notification-and-instanced-binding-semantics.md`

Load these as needed:

- `../../references/42-runtime-xaml-loader-and-dynamic-loading.md`
- `../../references/44-runtime-xaml-manipulation-and-service-provider-patterns.md`
- `../../references/49-adaptive-markup-and-dynamic-resource-patterns.md`
- `../../references/50-relative-static-resource-and-name-resolution-markup.md`
- `../../references/43-xaml-in-libraries-and-resource-packaging.md`

## Workflow

1. Default to compiled bindings with `x:DataType`.
2. Keep template, converter, and markup-extension wiring typed where possible.
3. Use runtime XAML loading only when dynamic loading is a real requirement.
4. Verify resource lookup, namescope, and relative binding assumptions explicitly.

## Rules

- Prefer generated or compiled binding paths over runtime parser fallbacks.
- Treat `RequiresUnreferencedCode` and runtime XAML APIs as explicit tradeoffs.
- Keep converters small and deterministic; prefer typed state or projection when possible.
- Use the generated API index only for signature lookup, not as the main workflow guide.
