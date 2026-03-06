# UI/UX Architecture Contract

**Date**: 2026-03-06 | **Status**: Governance Baseline | **Owner**: VoiceStudio.Core + VoiceStudio.App

This document defines the contract for panel registration, navigation, layout persistence, and file ownership. It is governance-only; no refactors are implied.

---

## PanelRegistry SSOT

`PanelRegistry` is the single source of truth for all panel definitions. No panel may be opened without prior registration via `PanelDescriptor`. All discovery (Command Palette, menus, workspace restore) MUST use `GetAllDescriptors()` or `TryGetDescriptor(panelId)`. The `_legacyPanelRegistry` in `MainWindow.xaml.cs` is **frozen** (see `.ci/ui_arch_legacy_allowlist.json`). No new entries may be added to the legacy registry.

**Enforcement**: CI enforces that the legacy allowlist cannot grow.

---

## OpenPanel(panelId) Only

All panel opens MUST go through a single entrypoint. Current implementation: `MainWindow.OpenPanelById(panelId)` which delegates to `PanelRegistry.CreatePanel(panelId)`. Future: expose `OpenPanel(panelId)` as the canonical API (e.g., on a service or MainWindow). No direct `CreatePanel` calls from navigation; routing is via `panelId` only.

---

## Workspace Layout Persistence Model

| Version | State | Description |
|---------|-------|--------------|
| **v1** | Current | 4-region layout — Left, Center, Right, Bottom (plus Floating). Persisted as JSON with `regions[]` containing `region`, `activePanelId`, `openedPanels[]`. See [WorkspaceDefinition](../../src/VoiceStudio.Core/Panels/WorkspaceDefinition.cs), [Studio.json](../../src/VoiceStudio.App/Resources/Workspaces/Studio.json). |
| **v2** | Next | Tabbed panels within regions. Requires ADR. Panels in same region shown as tabs; `PanelPlacement.Order` determines tab order. |
| **v3** | Future | Full docking tree. Requires ADR. Panels can be split, nested, or floated with position/size persistence. |

---

## Panel Metadata Contract

| Field | Type | Purpose |
|-------|------|---------|
| `Category` | `PanelCategory` enum | Grouping for menus/palette (General, Voice, Training, Audio, Settings, Diagnostics, Library, Effects, Automation, Other) |
| `Maturity` | `PanelMaturity` enum | Stability level (Stable, Beta, Experimental, Deprecated) |
| `Keywords` | `IReadOnlyList<string>?` | Search/filter terms for Command Palette. Optional; null by default. |

---

## Design System Usage Rules

- **Tokens:** All colors, spacing, typography MUST use VSQ.* tokens from [DesignTokens.xaml](../../src/VoiceStudio.Common.UI/Themes/DesignTokens.xaml). No hardcoded hex colors in panel XAML or code-behind.
- **Empty state:** Use `VSQ.EmptyState.TextBrush` for "no items" messages. Provide icon + message + optional action.
- **Loading state:** Use `VSQ.Loading.*` brushes; show progress indicator (ProgressRing/Bar) with `VSQ.Progress.ForegroundBrush`.
- **Error state:** Use `VSQ.Error.Brush` for error text; `VSQ.Warn.Brush` for warnings. Include retry/action where applicable.

---

## Rules

### Rule 1 — Single Panel Registry

All panels MUST be registered via `PanelDescriptor` in `PanelRegistry` (`src/VoiceStudio.App/Services/PanelRegistry.cs`). See PanelRegistry SSOT above.

### Rule 2 — Single Navigation Entrypoint

All panel opens MUST go through the single entrypoint. See OpenPanel(panelId) Only above. Direct `new XxxView()` in MainWindow is forbidden for **new** panels.

**Legacy exception**: Existing `new XxxView()` calls in MainWindow are quarantined via the allowlist. New panels must use the registry only.

### Rule 3 — Layout Persistence as JSON

Saved layout state MUST contain panel IDs only. No serialized View types, no binary blobs. Deserialization recreates panels via registry lookup by `panelId`.

### Rule 4 — Metadata-Driven Menu/Palette

Command Palette and menus MUST source panel entries from `PanelRegistry.GetAllDescriptors()`. `PanelDescriptor` includes `Category`, `Maturity`, and `Keywords` for grouping and search.

### Rule 5 — No `new View()` in MainWindow (Quarantined Allowlist)

No new `new XxxView()` instantiation in `MainWindow.xaml.cs` except those in the quarantined legacy allowlist (`.ci/ui_arch_legacy_allowlist.json`). CI can grep-enforce this.

**Legacy allowlist**: `.ci/ui_arch_legacy_allowlist.json` — frozen list of panel IDs from `_legacyPanelRegistry`. The list cannot grow.

### Rule 6 — Size Budget Policy

| File | Budget | Current |
|------|--------|---------|
| `MainWindow.xaml.cs` | 3,500 lines | 3,457 |
| `BackendClient.cs` | 3,954 lines | 3,954 |

New functionality goes into services or dedicated modules, not these files. Exceeding the budget requires an ADR.

---

## Forbidden Patterns

- **Direct `Activator.CreateInstance` outside PanelRegistry** — Panel creation is the exclusive responsibility of `PanelRegistry.CreatePanel()`.
- **`new XxxView()` in MainWindow outside allowlist** — All new panel instantiation must go through the registry.
- **Panel factories that bypass PanelDescriptor** — Registration must use `PanelDescriptor` with `PanelId`, `ViewType`, `DefaultRegion`, etc.
- **Hardcoded panel routing in switch/case blocks** — New panels must be discoverable via `GetAllDescriptors()`; routing is metadata-driven.

---

## File Ownership Boundaries

| File | Owner | Change Policy |
|------|-------|---------------|
| `PanelDescriptor.cs` | VoiceStudio.Core | Contract; changes require ADR |
| `IPanelView.cs` | VoiceStudio.Core | Contract; changes require ADR |
| `PanelRegistry.cs` | VoiceStudio.App | Sole implementation; changes require ADR |
| `MainWindow.xaml.cs` | VoiceStudio.App | Legacy host; **shrink only** |

---

## Customization Roadmap

| Phase | State | Description |
|-------|-------|-------------|
| **Regions** | Current | Left, Center, Right, Bottom, Floating — fixed regions |
| **Tabs** | Next | Tabbed panels within regions; requires ADR |
| **Docking tree** | Future | Full docking layout; requires ADR |

Each step beyond Regions requires an ADR.

---

## References

- **Legacy allowlist**: [.ci/ui_arch_legacy_allowlist.json](../../.ci/ui_arch_legacy_allowlist.json)
- **Panel registry**: [src/VoiceStudio.App/Services/PanelRegistry.cs](../../src/VoiceStudio.App/Services/PanelRegistry.cs)
- **Panel descriptor**: [src/VoiceStudio.Core/Panels/PanelDescriptor.cs](../../src/VoiceStudio.Core/Panels/PanelDescriptor.cs)
- **Panel system architecture**: [docs/developer/PANEL_SYSTEM_ARCHITECTURE.md](../developer/PANEL_SYSTEM_ARCHITECTURE.md)
