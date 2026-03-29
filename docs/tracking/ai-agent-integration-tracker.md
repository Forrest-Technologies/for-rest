# AI Agent Integration Tracker

## Objective

Wire a source-driven AI foundation into For-Rest using Microsoft Agent Framework, with:

- settings-backed provider/model/endpoint/API-key configuration
- canonical docs that feed human docs, Monaco help, search, and prompt context
- bounded local tools for docs search and document patch planning
- test coverage for settings, docs, search, patching, and runtime preparation

## Status

- `done` Docs/catalog canonical source created in `ForRestLanguageCatalog` + `ForRestLanguageReference`
- `done` Settings TOML flow extended for `[ai]` with masked API key behavior
- `done` Shared AI domain/services added under `src/ForRest.Services/AI`
- `done` Canonical docs now feed AI knowledge documents and prompt topics
- `done` Agent Framework runtime factory added for OpenAI and Azure OpenAI preparation
- `done` MAUI DI now registers AI services and can map persisted settings into shared AI runtime settings
- `done` Monaco inline AI conversation syntax and session routing
- `done` Safe active-document host tool wiring so the runtime can edit the live editor without raw text handoff
- `done` Active-document reads now include current compiler diagnostics so the agent can repair invalid requests without asking the user for syntax samples first
- `done` Active-document reads now also surface Roslyn/script-validation failures, so the agent no longer sees a false "no diagnostics" state when the request text compiles but generated scripts would still fail
- `done` AI rewrites now rebase request identity metadata so request title, slug/location, explorer selection, and generated summary stay in sync after a from-scratch rewrite
- `done` Blank `## ` placeholder prompts no longer hijack normal request sends
- `done` AI-edited requests now pass both ForRest compilation and Roslyn script validation before the edit is accepted, which blocks invalid generated flow like `expect` inside control-flow blocks
- `done` Inline AI now reopens a fresh `## ` near the active conversation instead of forcing the next prompt to the bottom of the document
- `done` After inline AI updates, the editor can move the caret back to the fresh `## ` prompt so the user stays in the same working area

## Decisions

- Use `Microsoft.Agents.AI.OpenAI` with `Azure.AI.OpenAI` for the runtime seam.
- Keep the real API key masked in the settings editor projection.
- Fresh disabled configs only show `ai.enabled`; advanced AI fields expand once enabled or once configuration exists.
- Use the canonical ForRest language catalog as the source for Monaco help, human docs, AI search, and prompt context.
- Keep the `responses` setting in configuration and prompt metadata, but currently prepare a chat-client runtime with a warning because the preview responses adapter stack is still version-sensitive.
- Use `##` for inline AI prompts, `#>` for active AI responses, and `#~` for faded historical AI responses inside request documents.
- Route `Send` to the AI when the cursor is on an inline AI conversation block or when a trailing `##` prompt is the last meaningful line in the document.
- Submit inline AI directly from the editor when the user presses `Enter` on a `##` prompt line, and flush Monaco text to the viewmodel before any send action to avoid stale-editor races on Android.
- Treat the active document plus its current diagnostics as the primary repair context; the agent should use docs and diagnostics before asking the user for grammar clarification.
- Treat inline IDE mode as action-first: the agent should pick reasonable defaults, preserve working `expect` syntax when present, and avoid turning simple rewrite/fix requests into questionnaires.

## Test Policy

- All new integration seams require unit tests first or alongside implementation.
- Current verification targets:
  - `tests/ForRest.Maui.Tests`
  - `tests/ForRest.Tests`
