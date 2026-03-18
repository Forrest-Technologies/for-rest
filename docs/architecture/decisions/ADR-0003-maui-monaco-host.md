# ADR-0003: MAUI Monaco Host Direction

- Status: Proposed
- Date: 2026-03-18

## Context

The Phase 1 shell needs code-like editing surfaces for request bodies, pre-request scripts, tests, and future response viewers, but the shell milestone should not be blocked on a full editor subsystem implementation.

The preferred integration direction for this MAUI reset is `flynk/maui-monaco`, which wraps Monaco Editor for .NET MAUI and exposes a native MAUI control surface for hosted editor features.

## Decision

Phase 1 will scaffold editor-host usage around a reusable `MonacoEditorHost` control in the MAUI app.

That control is a placeholder shell contract now and is intended to become the adapter boundary for `flynk/maui-monaco` once the repository or package is brought into the solution.

The eventual production path should wrap `flynk/maui-monaco` behind a local For-Rest control boundary, expose text/language/read-only/theme/layout operations through a narrow API, and reuse the same control for request body, script, tests, response body, and raw response tabs.

## Follow-Up

- Vendor or reference `flynk/maui-monaco` in the solution.
- Replace the placeholder internals of `MonacoEditorHost` with the real Monaco control.
- Add command routing for format, find, theme switching, and content-changed events.
