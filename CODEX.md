# For-Rest Codex Handoff

## Product Intent

For-Rest is a local-first, Windows-native REST client and API testing workbench. The product should feel closer to an IDE than a form-heavy utility: three panes, editor-forward request authoring, strong variable ergonomics, first-class scripting, and a clean trust boundary around local secrets, scripts, and future plugins.

## Current Baseline

Date: 2026-03-17

- Solution format: `ForRest.slnx`
- SDK baseline: `.NET 10.0.200-preview.0.26103.119` pinned in [`global.json`](global.json)
- App model: unpackaged WinUI 3 desktop app on Windows App SDK
- Primary storage: SQLite in `%LOCALAPPDATA%\\ForRest\\forrest.db`
- Portable override: `FORREST_DATA_DIR`

## Repo Map

```text
src/
  ForRest.App/                    WinUI shell and presentation view models
  ForRest.Domain/                 Variable resolution, request compilation, JSON helpers, extraction
  ForRest.Models/                 DTOs, enums, persisted models, execution records
  ForRest.Services/               Application services and HTTP execution pipeline
  ForRest.Repositories/           Repository contracts
  ForRest.Infrastructure.Sqlite/  SQLite persistence and DPAPI-backed secret protection
  ForRest.Scripting/              Roslyn script host and script-facing APIs
  ForRest.Plugins.Abstractions/   Versionable extension contracts
  ForRest.Plugins.Host/           Discovery scaffold
  ForRest.Shared/                 Shared result helpers

tests/
  ForRest.Tests/                 Unit and repository tests
```

## Commands

```powershell
dotnet build ForRest.slnx
dotnet test tests\ForRest.Tests\ForRest.Tests.csproj
Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe
```

## Architecture Rules

- Keep dependency flow downward only: `App -> Services -> Repositories -> Infrastructure`, with `Domain`, `Models`, `Scripting`, and plugin abstractions isolated from infrastructure concerns.
- Do not move business rules into views or XAML code-behind.
- Keep variable precedence explicit and test-covered.
- Keep scripting behind the host contract in `ForRest.Scripting`; do not leak UI or repository types into scripts.
- Treat secret values as secret by storage boundary, not by UI convention alone.
- Prefer permissive dependencies. Windows platform packages are accepted exceptions and are tracked in the dependency review docs.
- Preserve `file-scoped namespaces`, nullable enabled, primary constructors where they help DI-heavy classes, and `GlobalUsings.cs` per project.

## Progress Tracking

- The authoritative phase tracker is [`docs/architecture/mvp-progress.md`](docs/architecture/mvp-progress.md).
- If a phase materially changes, update that document in the same change set.
- If an architectural decision hardens, add or update an ADR under [`docs/architecture/decisions`](docs/architecture/decisions).

## Known Gaps After This Commit

- The WinUI shell now has custom chrome and a denser workbench header, but it still uses stock text surfaces instead of a dedicated editor subsystem.
- Explorer and request tabs are simplified compared with the final UX target.
- The visual language is materially closer to the target power-user workbench, but it still needs adjustable panes and the final editor/tooling pass. See [`docs/architecture/ui-direction.md`](docs/architecture/ui-direction.md).
- Repeat execution exists in the service layer, but the UI does not yet expose run-state metrics and preset management at the final quality bar.
- Plugin loading is intentionally scaffold-only.

## Next Priorities

1. Replace the request body and script text areas with a dedicated editor-hosting subsystem that supports syntax highlighting, gutters, completion, and diagnostics.
2. Harden the shell interaction model: tab closing, drag/reorder, richer explorer hierarchy, pane resizing, and denser keyboard flow.
3. Add extraction editing UI, response history inspection polish, and richer response viewers.
4. Add repository migration tests and a formal schema versioning path.
5. Expand ADR coverage for editor hosting, plugin versioning, theming tokens, and visual shell conventions.
