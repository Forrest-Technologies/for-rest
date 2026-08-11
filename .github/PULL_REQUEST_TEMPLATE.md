# Summary

<!-- One or two sentences: what does this PR do? -->

## What & why

<!-- What changed, and why it needed to change. Link related issues. -->

## Test evidence

- [ ] `dotnet build ForRest.slnx` passes
- [ ] `dotnet test tests/ForRest.Tests/ForRest.Tests.csproj` passes
- [ ] `dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj` passes
- [ ] Tested on at least one platform head (state which): <!-- Windows / Android / Mac Catalyst -->

## Style checklist

- [ ] Follows [CLAUDE.md](../CLAUDE.md) (file-scoped namespaces, primary constructors, regions, structured logging, `is null`, collection expressions, no `Async` suffix)
- [ ] Respects the downward dependency flow (App → Services → Repositories → Infrastructure); no business logic in views
- [ ] Does not touch vendored directories (`src/ForRest.Maui/Resources/Raw/monaco/`, `src/ForRest.Maui/roslyn-runtime/`)
- [ ] If it touches secret storage, `ForRest.Mcp`, or `ForRest.Scripting`: flagged for human security review

## AI assistance disclosure

- [ ] This PR was authored or assisted by an AI tool
  - Tool used: <!-- e.g. Claude Code, Codex, Copilot — or n/a -->
- [ ] A human reviewed and tested this change
