# ADR-0002: Editor Subsystem Direction

- Status: Accepted
- Date: 2026-03-16

## Context

For-Rest depends heavily on request body editing, response inspection, scripting, assertions, and eventually request/response manipulation workflows that will need far stronger editing capabilities than stock text controls provide.

## Decision

The current WinUI text surfaces are an interim shell baseline only. The product direction requires a dedicated editor subsystem with IDE-grade capabilities for:

- JSON and raw body editing
- pre-request and test scripts
- response inspection surfaces where syntax and structure matter

The subsystem must support syntax highlighting, gutters, diagnostics, and completion-friendly scripting ergonomics.

## Consequences

### Positive

- The architecture stays honest about a product-critical requirement.
- Future scripting and attack-surface workflows have a viable path.
- The shell can still evolve now without pretending the editor problem is solved.

### Negative

- The current shell remains visually ahead of its editor capability until the subsystem lands.
- A later integration step will be required to replace transitional text surfaces.

## Follow-Up

- evaluate hosting options that are compatible with WinUI 3 and commercial distribution goals
- define how the script API surfaces into completion and diagnostics
- add an implementation ADR once the concrete editor technology is chosen
