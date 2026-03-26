# Panel Contract Enforcement Plan

**Status:** Planning (no code until reviewed)  
**Date:** 2026-03-21  
**Related:** [FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md](FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md) Rank 4, [IPanelView](src/VoiceStudio.Core/Panels/IPanelView.cs), [TEST_CLASSIFICATION.md](../governance/TEST_CLASSIFICATION.md)

---

## 1. Registry Descriptor Shape

### Current State

[PanelDescriptor](src/VoiceStudio.Core/Panels/PanelDescriptor.cs) defines:

| Property | Purpose |
|----------|---------|
| PanelId | Unique identifier |
| DisplayName | UI label |
| DefaultRegion | Left, Center, Right, Bottom |
| ViewType | UserControl type |
| ViewModelType | Optional; resolved via IViewModelFactory |
| Icon, Description | Metadata |
| Category | PanelCategory enum (General, Voice, Training, Audio, etc.) |
| MenuCategory | Modules menu grouping |
| Maturity | Stable, Beta, Experimental, Deprecated |
| Keywords | Command Palette search terms |
| IsVisible | Hidden panels (dead-route) |

### Gaps (To Define)

- **Capabilities** — No explicit capability flags (e.g. requires-backend, requires-timeline, read-only). Panels are implicitly capable by ViewType.
- **Required services** — No declaration of which services a panel needs (IBackendClient, IProfilesClient, IEventAggregator, etc.). Dependency is implicit in ViewModel constructor.
- **Test bucket classification** — No per-panel classification (seam vs integration vs workflow proof). See TEST_CLASSIFICATION.md for definitions.

### Proposed Additions (Future)

- `IReadOnlyList<string>? RequiredServiceNames` — Declare dependencies for validation.
- `PanelTestBucket TestBucket` — Seam, Integration, or Workflow per panel category.

---

## 2. Lifecycle Ownership

### Who Creates Panels

| Component | Role |
|-----------|------|
| **PanelRegistry** | Creates panel instances via `CreatePanel(panelId)`; uses `PanelDescriptor.ViewType` and `ViewModelType`; resolves ViewModel via `IViewModelFactory`. |
| **PanelHost** | Requests panel from registry or legacy factory; hosts the UserControl; owns the loaded instance in `_loadedPanels`. |
| **CorePanelRegistrationService**, **AdvancedPanelRegistrationService**, **ModulePanelRegistrationService** | Register `PanelDescriptor` entries into `PanelRegistry` at startup. |

### Who Disposes Panels

- **PanelHost** — When switching panels, previous content is replaced. No explicit `Dispose` on UserControl; WinUI GC handles. `PanelLoader` (deprecated) had `Dispose`; `PanelRegistry` does not dispose created instances.
- **EffectsMixerViewModel** — Implements `IDisposable`; disposes internal resources in `OnDeactivatedAsync`.

### Who Calls OnActivatedAsync / OnDeactivatedAsync

- **PanelHost** ([PanelHost.xaml.cs](src/VoiceStudio.App/Controls/PanelHost.xaml.cs) lines 336–379) — When content changes:
  - `ActivateViewModelAsync` — Calls `lifecycle.OnActivatedAsync(ct)` if ViewModel implements `IPanelLifecycle`.
  - `DeactivateViewModelAsync` — Calls `lifecycle.OnDeactivatedAsync(ct)` before replacing content.
- **TagManagerView.xaml.cs**, **ProsodyView.xaml.cs** — Call `OnActivatedAsync` directly (code-behind; inconsistent with PanelHost-driven flow).

### Lifecycle Flow

```
PanelHost.SetContent(panel) 
  → DeactivateViewModelAsync(previous) 
  → ActivateViewModelAsync(new)
```

---

## 3. Navigation Ownership

### Extracted

- **ShellNavigationCoordinator** — `OpenPanelByIdAsync`, `ExecuteNavCommandAsync`, `ResolvePanelIdAlias`, `GetPanelRegion`, `GetPanelTitle`. MainWindow delegates nav to coordinator.

### Remaining Gaps

- **Direct panel opens** — Some code paths may open panels without going through `ShellNavigationCoordinator` (e.g. Command Palette, Modules menu). Audit required.
- **Nav rail vs Command Palette** — Both can trigger panel switches; ownership of "which panel is active" may be split.
- **Legacy panel registry** — MainWindow retains `_legacyPanelRegistry` for panels not yet in `PanelRegistry`; `ShellNavigationCoordinator` uses `id => _legacyPanelRegistry.TryGetValue(id, out var e) ? e.Factory : null`.

---

## 4. Backend Seam Ownership

### Current Pattern

- **Migrated panels** — Use narrow `I*Client` (e.g. `IProfilesClient`, `ITrainingClient`, `ITimelineClipService`). See seam-aware tests in TEST_CLASSIFICATION.md.
- **Non-migrated panels** — Still inject `IBackendClient` directly.

### Panels to Identify

- Audit ViewModels for `IBackendClient` vs `I*Client` usage.
- Panels reaching through `IBackendClient` directly: document; prioritize for seam migration.
- Reference: [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [IBACKENDCLIENT_LONGTAIL_RANKING.md](IBACKENDCLIENT_LONGTAIL_RANKING.md).

---

## 5. Event Subscription Ownership

### Who Subscribes

- ViewModels implementing `IPanelLifecycle` subscribe in `OnActivatedAsync` (e.g. `LibraryViewModel`, `ScriptEditorViewModel`).
- `IEventAggregator` — Used by LibraryViewModel, ScriptEditorViewModel, VoiceCloningWizardViewModel, JobProgressViewModel, others.

### Who Unsubscribes

- `OnDeactivatedAsync` — Should unsubscribe. Examples:
  - **LibraryViewModel** — `OnDeactivatedAsync` unsubscribes (line 354).
  - **ScriptEditorViewModel** — `OnDeactivatedAsync` unsubscribes selection (line 207).
  - **MCPDashboardViewModel** — `OnDeactivatedAsync` unsubscribes (line 130).
- Many panels implement `OnDeactivatedAsync` as `Task.CompletedTask` — **no unsubscribe**. Risk of leaks.

### Where Leaks Are Likely

- Panels that subscribe in `OnActivatedAsync` but do not unsubscribe in `OnDeactivatedAsync`.
- Panels that subscribe in constructor (prohibited per ADR-047) — constructor fire-and-forget.
- Long-lived event handlers holding references to disposed or inactive panels.

---

## 6. Test Classification

### Definitions (from TEST_CLASSIFICATION.md)

| Classification | Definition | Supports "Migration Complete"? |
|----------------|-------------|--------------------------------|
| **Seam-aware** | Instantiates target ViewModel with migrated seam interfaces. Exercises migrated path. | Yes |
| **Transport-mock** | Mocks IBackendClient/HTTP; does not instantiate migrated ViewModel. | No |
| **Legacy** | Bypasses seam; tests old transport patterns or DTOs only. | No |

### Per-Panel Category

| Panel Category | Recommended Test Bucket | Notes |
|----------------|-------------------------|-------|
| Core (Timeline, Mixer, Profiles) | Seam-aware + integration | High traffic; must prove migrated path. |
| Training, Transcribe, Library | Seam-aware | Per TEST_CLASSIFICATION; seam tests exist. |
| Settings, Diagnostics | Seam or integration | Lower churn. |
| Experimental / Beta | Seam or smoke | Maturity = Beta/Experimental. |

### Constructor Invariant (ST-02)

- Migrated ViewModels must have `Constructor_DoesNotCallClient_BeforeActivation` (or justified variant).
- Verification: `python scripts/ci/check_constructor_invariant_coverage.py`.

---

## Next Steps (After Plan Review)

1. Add capabilities / required-services to `PanelDescriptor` (optional; can defer).
2. Audit panels for `IBackendClient` vs `I*Client`; update SEAM_MATURITY_AUDIT.
3. Audit `OnDeactivatedAsync` for missing unsubscribes; fix leaks.
4. Consolidate `OnActivatedAsync` calls (TagManagerView, ProsodyView) to PanelHost-only flow.
5. Document legacy vs registry panel split; plan migration of legacy to registry.

---

## Changelog

| Date       | Change |
|------------|--------|
| 2026-03-21 | Initial plan; 6 sections; no code. |
