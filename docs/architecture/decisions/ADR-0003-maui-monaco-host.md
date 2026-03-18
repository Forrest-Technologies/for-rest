# ADR-0003: MAUI Editor Host Direction

- Status: Proposed
- Date: 2026-03-18

## Context

The Phase 1 shell needs code-like editing surfaces for request bodies, pre-request scripts, tests, and future response viewers, but the shell milestone should not be blocked on a full editor subsystem implementation.

The preferred follow-on integration direction for this MAUI reset is `flynk/maui-monaco`, which wraps Monaco Editor for .NET MAUI and exposes a native MAUI control surface for hosted editor features.

## Decision

Phase 1 uses a reusable `EditorSurface` control backed by the built-in MAUI `Editor` so the shell remains editable and launch-safe while the pane system is being tuned.

That control is the temporary shell contract now and is intended to become the adapter boundary for `flynk/maui-monaco` once the repository or package is brought into the solution.

The eventual production path should wrap `flynk/maui-monaco` behind a local For-Rest control boundary, expose text, language, read-only, theme, and layout operations through a narrow API, and reuse the same control for request body, script, tests, response body, and raw response tabs.

## Follow-Up

- Vendor or reference `flynk/maui-monaco` in the solution.
- Replace the placeholder internals of `EditorSurface` with the real Monaco-backed host or wrap Monaco behind a sibling adapter with the same public surface.
- Add command routing for format, find, theme switching, and content-changed events.
