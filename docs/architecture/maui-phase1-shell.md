# MAUI Phase 1 Shell

Date: 2026-03-18

This document supersedes earlier MAUI shell notes that described the product like a traditional REST client or a mostly form-driven tool.

## Phase 1 Objective

Phase 1 is a shell reconstruction project.

The goal is not to build the final platform, scripting engine, or full request execution workflow. The goal is to establish the correct product shell: a fluid, text-based, three-pane engineering workbench whose identity is shell-first and editor-first.

## Product Identity

For-Rest is not meant to feel like Postman, Insomnia, or a generic forms-over-data client.

It is also not meant to read like a VS Code clone.

The intended category is a serious API workbench for engineers where pane relationships, text surfaces, and editor-grade workflows define the experience.

## Shell Rules

- The pane system is the product shell, not a cosmetic layout choice.
- The left pane is for navigation, organization, and workspace-style exploration.
- The center pane is the dominant authoring surface and must feel editor-first.
- The right pane is for inspection, output, response, and feedback.
- Each pane is a tab-capable host even when the underlying functionality is still placeholder content.

## Visual Direction

- Light-first
- Text-first
- Tool-first
- Compact
- Structured
- Readable
- Neutral with restrained accenting

The shell should communicate through text, tabs, editor surfaces, dense rows, dividers, and structured outputs.

The shell should not depend on oversized cards, decorative dashboard panels, loud color, large action buttons, or a stack of conventional form inputs as the main interaction model.

## Windows-First Behavior

On Windows, the default shell must show all three panes at once.

- Left and right panes are visible by default.
- The center pane remains dominant.
- Splitters are draggable.
- Left and right panes are collapsible and restorable.
- Resizing must remain stable and orienting, not disruptive.

## Android Direction For Now

Android is not the focus of this phase, but the product model stays the same.

The same three-pane identity should remain conceptually true even when the panes collapse into overlay behavior on smaller widths.

## Temporary Editor Contract

Phase 1 uses a temporary editor host so the shell can be tuned before Monaco integration lands.

That temporary host is acceptable only because it preserves the center-pane contract:

- request authoring is text-based
- scripts belong in the center pane
- tests belong in the center pane
- body editing belongs in the center pane
- richer editor behavior is expected later behind the same control boundary

## Stop Condition

Phase 1 stops when the MAUI app presents a convincing three-pane workbench shell with the right proportions, tab structure, density, and editor-first feel.

Feature layering beyond that should wait until the shell feels correct.
