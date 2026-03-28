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
- `pending` Monaco inline AI conversation syntax and session routing
- `pending` Safe active-document host tool wiring so the runtime can edit the live editor without raw text handoff

## Decisions

- Use `Microsoft.Agents.AI.OpenAI` with `Azure.AI.OpenAI` for the runtime seam.
- Keep the real API key masked in the settings editor projection.
- Fresh disabled configs only show `ai.enabled`; advanced AI fields expand once enabled or once configuration exists.
- Use the canonical ForRest language catalog as the source for Monaco help, human docs, AI search, and prompt context.
- Keep the `responses` setting in configuration and prompt metadata, but currently prepare a chat-client runtime with a warning because the preview responses adapter stack is still version-sensitive.

## Test Policy

- All new integration seams require unit tests first or alongside implementation.
- Current verification targets:
  - `tests/ForRest.Maui.Tests`
  - `tests/ForRest.Tests`
