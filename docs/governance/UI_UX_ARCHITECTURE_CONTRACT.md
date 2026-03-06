# UI/UX Architecture Contract

**Date**: 2026-03-06 | **Status**: Governance Baseline | **Owner**: VoiceStudio.Core + VoiceStudio.App

This document defines the contract for panel registration, navigation, layout persistence, and file ownership. It is governance-only; no refactors are implied.

---

## Rules

### Rule 1 — Single Panel Registry

All panels MUST be registered via `PanelDescriptor` in `PanelRegistry` (`src/VoiceStudio.App/Services/PanelRegistry.cs`). The `_legacyPanelRegistry` in `MainWindow.xaml.cs` is **frozen** (see `.ci/ui_arch_legacy_allowlist.json`). No new entries may be added to the legacy registry.

**Enforcement**: CI enforces that the legacy allowlist cannot grow.

### Rule 2 — Single Navigation Entrypoint

All panel opens MUST go through `PanelRegistry.CreatePanel(panelId)` (or the future `OpenPanel(panelId)` when implemented). Direct `new XxxView()` in MainWindow is forbidden for **new** panels.

**Legacy exception**: Existing `new XxxView()` calls in MainWindow are quarantined via the allowlist. New panels must use the registry only.

### Rule 3 — Layout Persistence as JSON

Saved layout state MUST contain panel IDs only. No serialized View types, no binary blobs. Deserialization recreates panels via registry lookup by `panelId`.

### Rule 4 — Metadata-Driven Menu/Palette

Command Palette and menus MUST source panel entries from `PanelRegistry.GetAllDescriptors()`. `PanelDescriptor` will gain `Category` and `Maturity` fields (future work; documented as roadmap item).

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
