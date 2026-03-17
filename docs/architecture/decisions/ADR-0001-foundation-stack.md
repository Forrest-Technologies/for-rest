# ADR-0001: Foundation Stack

- Status: Accepted
- Date: 2026-03-16

## Context

For-Rest needs to be a Windows-native, local-first desktop application with a clear path to commercial distribution, testability, strong offline behavior, and future extensibility for scripts and plugins.

## Decision

The foundation stack for the initial product is:

- .NET 10 preview pinned in `global.json`
- WinUI 3 with Windows App SDK for the desktop shell
- unpackaged app model for repeatable CLI build and run loops during early development
- SQLite for local persistence
- DPAPI for protecting secret values at rest
- Roslyn C# scripting for the first script host implementation
- `slnx` as the solution format

## Consequences

### Positive

- The app stays aligned with a Windows-native user experience.
- Core logic remains testable outside the UI.
- Local-only persistence is simple and fast.
- The script host matches the product requirement for C#-style scripting ergonomics.
- The solution structure maps directly to the intended architecture boundaries.

### Negative

- Windows App SDK and Windows SDK build tools introduce Microsoft platform licenses that are not purely permissive.
- Roslyn scripting increases dependency weight and requires explicit trust communication.
- The current shell still needs a dedicated editor subsystem before it reaches the desired workstation feel.

## Follow-Up ADRs

- editor hosting strategy
- schema versioning and migrations
- plugin contract versioning
- semantic theme token system
