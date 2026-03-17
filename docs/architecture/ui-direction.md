# UI Direction

Date: 2026-03-17

## Visual Goal

For-Rest should feel like a serious developer workstation with a modern Windows-native edge:

- sleek and fluid, but not soft
- dense and utilitarian, but not cramped
- futuristic in restraint, not neon theater
- clearly optimized for developers, testers, and security researchers who live in panes, tabs, and keyboard flow

The reference direction is closer to an IDE-grade API workbench than a generic desktop CRUD app.

## Shell Principles

### Pane Utility First

- three panes remain the default mental model
- pane widths must be adjustable
- pane headers should communicate purpose immediately
- action density should be higher than a consumer app

### Developer Tooling Feel

- monospaced editing surfaces should look intentional
- request, script, and response areas should read as code tooling
- semantic color should highlight method, status, trust, and runtime state
- tabs and lists should look compact and operational

### Modern Native Tone

- use layered dark surfaces, fine borders, and restrained accent lighting
- prefer subtle gradients and materials over flat blocks
- keep typography crisp and readable for long sessions
- avoid decorative color noise
- remove the stock-looking title bar from the visual hierarchy and fold window chrome into the workbench shell

## Current Baseline After This Pass

- custom WinUI title bar is integrated into the shell so the window chrome no longer fights the product identity
- the top request strip is now a custom workbench surface instead of the stock command bar look
- request and response panes expose execution telemetry more explicitly: method, host, status, time, size, type, and response details
- seeded workspace requests now demonstrate variable-based URLs and header-aware execution more clearly

## Editor Subsystem Direction

The current stock WinUI text surfaces are transitional only.

The target editor subsystem must support:

- syntax highlighting for JSON, HTTP-ish content, and C# scripts
- line numbers and gutters
- find and replace
- bracket and indentation behavior
- diagnostics and error markers
- completion or IntelliSense-style affordances for the scripting API
- extensibility for future proxy/mitm manipulation flows and richer viewers

The implementation may be a hosted editor component or a native/editor hybrid, but it must behave like a real code surface.

## Expected Interaction Tone

- fast opening and switching between requests
- keyboard-forward request execution and inspection
- compact explorer with strong method/status markers
- request tabs that feel like documents, not wizard pages
- output panes that can hold console, tests, history, and raw payload work without visual collapse

## What This Means For Ongoing Work

When touching the shell:

- bias toward denser pane chrome and clearer information hierarchy
- preserve semantic colors and code-friendly typography
- avoid business-app spacing and generic form layout whenever possible
- prefer changes that move the shell toward an IDE workbench, even if the full editor host is not in place yet
- prefer custom shell surfaces over generic command bars or obviously stock desktop chrome when the default look undercuts the product tone
