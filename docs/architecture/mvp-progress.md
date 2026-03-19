# MVP Progress

Last updated: 2026-03-18

## Status Summary

| Phase | Status | What exists now | What remains |
| --- | --- | --- | --- |
| Phase 0, foundation | Complete | `ForRest.slnx`, layered projects, nullable and analyzer baseline, DI/logging setup, test project, architecture and dependency docs scaffold | Add more analyzers and CI later |
| Phase 1, shell and persistence | In progress | WinUI shell, workspace switching, SQLite app state and execution history repositories, DPAPI-backed secret persistence, `System` theme resolution, workspace-driven theme application, custom title bar integration, persisted pane layout, and a restrained IDE-like three-pane workbench | richer explorer behavior, schema versioning, more preference persistence |
| Phase 2, request authoring | In progress | method, URL, params, headers, auth, body, variables, script, and schedule surfaces in the shell; dirty-state document tabs; compact authoring tabs; response inspector tabs; and code-surface styling for body/script/response editing | dedicated editor subsystem, stronger row editing, duplication/move/delete flows, bulk paste, inline diagnostics |
| Phase 3, execution and response | In progress | request compilation, HTTP execution pipeline, response snapshotting, raw response, history storage, status/duration/size summary, and a loopback test proving method/header/body request execution against a local HTTP listener | timeline visualization, richer history inspection, better cancellation and run-state UX |
| Phase 4, variables and environments | Substantial | explicit precedence, environment switching, variable preview, interpolation across request/auth/body/scripts, response extraction, secret variable protection | richer variable management UI and extraction authoring UI |
| Phase 5, scripting and tests | Substantial | pre-request and tests scripts, Roslyn host, tests API, console API, runtime variable mutation, automated coverage | dedicated script editor ergonomics, stronger script diagnostics, guardrails for long-running scripts |
| Phase 6, repeat runner and automation | Partial | repeat runner service, delay and interval model, schedule fields in the UI | cancellation/status visualization, metrics dashboard, saved preset management UX |
| Phase 7, extensibility scaffold | Scaffolded | plugin abstractions and discovery scaffold | plugin manifests, trust UX, enable/disable flows, version negotiation |

## What This Commit Delivers

- A buildable slnx-based solution with the requested project boundaries.
- A functional WinUI 3 shell wired to the current services.
- A refreshed shell pass that moves the app toward a denser IDE-like power-user presentation with real document tabs, persisted panes, and dual-theme parity.
- Local SQLite persistence with secret protection.
- A real request execution pipeline instead of UI-only stubs, now covered by a local loopback request/response test.
- First-class script hosting and test result capture.
- A non-trivial automated test suite covering the current critical logic.

## Not Yet at the Final MVP Bar

- The shell still uses standard text controls instead of a purpose-built editor subsystem.
- Request tree and row-management behaviors are still simplified compared with the product brief.
- Scheduling exists mostly as a service capability rather than a polished operator workflow.
- Plugin support is architecture-only for now.
- The current visual shell is much closer to the target power-user look, but the final editor/tooling experience still depends on the dedicated editor subsystem and richer explorer/history workflows.

## Next Slice Recommendation

1. Editor hosting ADR and implementation spike.
2. Request explorer, history rail, and document-tab hardening.
3. Extraction editor and response history improvements.
4. Schema versioning and migration tests.
5. Plugin manifest contract draft.
