# Contributing to For-Rest

Thanks for wanting to make For-Rest better. Contributions from humans and AI agents are both welcome — the rules below apply to everyone, with an extra section for agents at the end.

## Getting started

For-Rest is a .NET 10 MAUI app. The SDK version is pinned — install `.NET 10.0.200-preview.0.26103.119` (or let `global.json` resolve it) before anything else.

```bash
# Build the whole solution (ForRest.slnx is the solution file)
dotnet build ForRest.slnx

# Run both test projects
dotnet test tests/ForRest.Tests/ForRest.Tests.csproj
dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj
```

### Platform heads

- **Windows:** `dotnet build ForRest.slnx`, then `Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe`
- **Android:** `dotnet build src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-android` (on a macOS host, add `-p:TargetFrameworks=net10.0-android` and install the Android SDK — macOS hosts default to the Mac Catalyst head)
- **Mac Catalyst (unsigned, no Apple Developer license needed):**
  `dotnet publish src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-maccatalyst -c Release -p:ForRestMacUnsigned=true`

If you only have one platform available, that's fine — say which one you tested on in your PR.

## Code style

[CLAUDE.md](CLAUDE.md) is the authoritative C# style guide for this repo. Read it before writing code. The short version:

- File-scoped namespaces (`namespace ForRest.Services;`), nullable enabled everywhere, `GlobalUsings.cs` per project.
- Primary constructors for DI-heavy classes; fields are `camelCase` with no underscore prefix.
- No `Async` suffix on async methods unless a sync twin exists.
- Classes organized with `#region` blocks (Private Fields → Properties → Public Methods → Private Methods).
- Structured logging via injected `ILogger<T>` — never `Console.WriteLine`, never `$"..."` interpolation in log messages.
- `is null` for null checks; collection expressions (`[]`, `[..items]`); target-typed `new()`.
- No `.Result` / `.Wait()`; no swallowed exceptions.

## Architecture rules

These are hard constraints, not suggestions:

- **Strict downward dependency flow:** `App → Services → Repositories → Infrastructure`, with `Domain`, `Models`, `Scripting`, and the plugin abstractions isolated from infrastructure concerns. Never reference upward.
- **No business logic in views** or XAML code-behind — it belongs in services and view models.
- **Scripting stays behind the host contract** in `src/ForRest.Scripting/`. Do not leak UI or repository types into the script-facing API surface.
- **Secrets are secret by storage boundary**, not by UI convention. Secret values are encrypted in `src/ForRest.Infrastructure.Sqlite/` (DPAPI on Windows) and redacted before any AI provider or MCP client sees them. Nothing you add may weaken that boundary.
- Keep variable precedence (System → Global → Workspace → Environment → Request-local → Runtime) explicit and test-covered.

## Pull request process

1. Branch from `develop` (it's the default branch and the PR target).
2. Keep PRs small and focused — one behavior change per PR. No drive-by refactors bundled with a feature.
3. `dotnet build ForRest.slnx` and both test projects must pass before you open the PR.
4. Describe **what** changed and **why**, not just how. The [PR template](.github/PULL_REQUEST_TEMPLATE.md) walks you through it.
5. Never force-push to `develop`. Force-pushing your own feature branch is fine.

## AI-agent contributors

Agents are welcome here — For-Rest is itself agent-ready software, and agent-authored PRs are a normal part of this repo. The ground rules:

- **Read [AGENTS.md](AGENTS.md) and [CLAUDE.md](CLAUDE.md) first.** They encode the conventions your diff will be judged against.
- **Disclose the tooling.** Agent-authored PRs must state which tool produced the change (Claude Code, Codex, Copilot, etc.) and confirm that a human reviewed and tested it. The PR template has a checkbox section for this.
- **Run the full build and both test projects** (`dotnet build ForRest.slnx`, `tests/ForRest.Tests`, `tests/ForRest.Maui.Tests`) before opening a PR — not just the tests near your change.
- **Security-sensitive areas need explicit human sign-off** before merge: secret storage and DPAPI protection in `src/ForRest.Infrastructure.Sqlite/`, MCP server auth in `src/ForRest.Mcp/`, and the scripting sandbox in `src/ForRest.Scripting/`. Flag these changes prominently in the PR description.
- **No drive-by mass-refactors.** An agent PR that reformats or restructures files it didn't need to touch will be closed.

## Licensing of contributions

For-Rest is [MIT licensed](LICENSE). Contributions are accepted on the same inbound = outbound terms: by submitting a PR you agree your contribution is licensed under MIT. There is no CLA to sign.
