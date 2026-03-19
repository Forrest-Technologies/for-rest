# UI Direction

Date: 2026-03-18

This document replaces earlier product-description and UI guidance that framed For-Rest as a more conventional REST client surface.

## Core Direction

For-Rest should present as a fluid engineering workbench.

The interface is centered on three distinct working regions:

- left pane for navigation and organization
- center pane for primary authoring and scripting surfaces
- right pane for output, inspection, response, and feedback

These regions must read as different responsibilities, not as three equal cards on a page.

## Center Pane Priority

The center pane is the most important region.

It must not be treated like a stack of forms, toggles, and large buttons.

It must instead read like the main editor surface where future scripting, request authoring, and advanced logic will live. Even before Monaco is integrated, the shell should already make this obvious.

## Interaction Tone

- fluid and stable during resize
- compact and work-oriented
- textual and editor-led
- restrained in color and chrome
- deliberate about pane boundaries and tab hierarchy

The app should feel closer to an IDE, database client, or engineering workbench than to a dashboard or a mobile-first CRUD experience.

## Visual Language

- light-first by default
- neutral surfaces with restrained azure-like accents
- dense rows instead of oversized list tiles
- tabs and text instead of decorative containers
- subtle dividers instead of dramatic chrome
- structured outputs instead of colorful widgets

Avoid:

- large card sections
- dashboard styling
- form-heavy request editing
- oversized command bars
- flashy effects
- stylized hacker aesthetics
- obvious VS Code imitation

## Pane Expectations

### Left Pane

The left pane should feel textual and navigational. It should rely on compact rows, subtle grouping, and light selection treatment rather than big action buttons or chunky control trays.

### Center Pane

The center pane should remain dominant and editor-first. Tabs in this region are future-facing hosts for request, body, script, test, and variable work.

### Right Pane

The right pane should feel like a real companion region, already prepared for response viewing, raw output, headers, logs, and other inspection surfaces.

## Ongoing Rule

When making UI changes, prioritize shell clarity, pane identity, density, and text-first workflow over feature breadth.

If a design choice starts pulling the app back toward a traditional forms-first REST client, that choice is moving in the wrong direction.
