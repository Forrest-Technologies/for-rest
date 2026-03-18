# MAUI Phase 1 Shell

Date: 2026-03-18

## Pane System Choices

- The MAUI shell is isolated in `src/ForRest.Maui` so the reset to a MAUI-first shell does not rewrite the existing WinUI work in place.
- The workbench uses a five-column grid: left pane, left splitter, center pane, right splitter, right pane.
- Left and right panes use absolute pixel widths while the center pane remains star-sized so the active drafting surface stays dominant.
- Splitters are implemented with `PanGestureRecognizer` and clamp against minimum left, center, and right widths to keep the layout usable while dragging.
- Collapse and expand are stateful in the page view-model so width restoration later can be persisted without reworking the layout model.
- The shell is forced to a light theme in Phase 1 to keep the visual direction aligned with the requested white, textual reference.
- The command lane stays compact and request-focused, while the full center and right tab sets are present as shell placeholders so the pane system feels real without pulling in deeper product work.
- On narrow widths and Android-sized layouts, the same explorer/workbench/inspector model switches to overlay panes so the product keeps its pane-workbench identity instead of becoming a separate mobile navigation pattern.

## Why This Fits Phase 1

- It delivers a real pane system now instead of static columns.
- It keeps the shell compact, text-first, and obvious about pane boundaries.
- It avoids speculative infrastructure while leaving a clean path for persisting workspace-specific pane widths later.
- It keeps the editing surfaces usable now with MAUI `Editor` controls, which is enough to validate the shell before replacing internals with a richer editor host.
- It lets Windows behave like a desktop tool by default while still giving Android a clear adaptation path without changing the product model.
