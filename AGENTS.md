# AGENTS.md

Operational guide for coding agents (Claude, Codex, Copilot, or any other) working in this repo. This file is the canonical agent entry point; [CLAUDE.md](CLAUDE.md) and [CODEX.md](CODEX.md) carry the full tool-specific style guide and conventions.

## What this is

For-Rest is a local-first API workbench: a .NET 10 MAUI app (Windows, Android, Mac Catalyst) where HTTP requests are plain-text `.frs` scripts compiled to C# on a Roslyn host, with local SQLite storage, encrypted secrets, and a built-in MCP server so agents can drive the workbench itself.

## Build and test

SDK is pinned to `.NET 10.0.200-preview.0.26103.119` via `global.json`.

```bash
dotnet build ForRest.slnx
dotnet test tests/ForRest.Tests/ForRest.Tests.csproj
dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj
```

Platform heads: Windows (`src/ForRest.App`), Android and Mac Catalyst (`src/ForRest.Maui`). On macOS hosts the MAUI project defaults to the Catalyst head; pass `-p:TargetFrameworks=net10.0-android` to build Android. Unsigned Mac build: `dotnet publish src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-maccatalyst -c Release -p:ForRestMacUnsigned=true`.

Run **all** of the above before opening a PR, not just the tests near your change.

## Architecture rules (hard constraints)

- Dependency flow is downward only: `App → Services → Repositories → Infrastructure`; `Domain`, `Models`, `Scripting`, and plugin abstractions stay isolated from infrastructure. Never reference upward.
- No business logic in views or XAML code-behind.
- Scripting stays behind the host contract in `src/ForRest.Scripting/` — no UI or repository types in the script-facing surface.
- Secrets are secret by storage boundary (DPAPI in `src/ForRest.Infrastructure.Sqlite/`), redacted before any AI/MCP client sees them. Never weaken this.
- Variable precedence (System → Global → Workspace → Environment → Request-local → Runtime) stays explicit and test-covered.

## C# style essentials

Full guide in [CLAUDE.md](CLAUDE.md) — read it. Summary: file-scoped namespaces; nullable enabled; primary constructors for DI; `camelCase` fields with no underscore prefix; no `Async` suffix unless a sync twin exists; `#region` organization; structured logging with injected `ILogger<T>` (no `Console.WriteLine`, no interpolated log messages); `is null`; collection expressions `[]`; no `.Result`/`.Wait()`; `GlobalUsings.cs` per project.

## Never touch

- `src/ForRest.Maui/Resources/Raw/monaco/` — vendored Monaco editor (updated via `scripts/Update-Monaco.ps1`)
- `src/ForRest.Maui/roslyn-runtime/` — vendored runtime assemblies for the Roslyn script host

## Security-sensitive areas (human sign-off required)

Changes here need explicit human review before merge — flag them prominently in the PR:

- `src/ForRest.Infrastructure.Sqlite/` — secret storage and DPAPI protection
- `src/ForRest.Mcp/` — MCP server exposure and auth
- `src/ForRest.Scripting/` — script execution host and its sandbox boundary

## PR etiquette

From [CONTRIBUTING.md](CONTRIBUTING.md): branch from `develop`; small focused PRs; describe what + why; disclose the agent tool used and that a human reviewed/tested the change; no force-pushes to `develop`; no drive-by mass-refactors.
