# GOV-VOICESTUDIO-GAP005-ORPHANED-FEATURE-INTEGRATION-02

**Status:** Closed
**GAP:** GAP-005 - Orphaned non-duplicate feature capability integration
**Phase:** 0 (Broken)
**Role:** System Architect / UI Engineer
**Created:** 2026-04-08

---

## Problem Statement

GAP-005 closed as a delete-only lane and removed all `Features/*` files. A corrective directive now governs GAP-005:

- Duplicates/replaced systems remain deleted.
- Dead parallel architectures remain deleted only when a real improved replacement exists.
- Non-duplicate capabilities are not allowed to disappear and must be represented in canonical app architecture.

The following nine non-duplicate capabilities must be restored or canonically re-homed:

- `Features/Toolbar/ToolbarViewModel.cs`
- `Features/Accessibility/AccessibilityService.cs`
- `Features/Notifications/NotificationService.cs`
- `Features/Panels/PanelManager.cs`
- `Features/PowerUser/KeyboardShortcuts.cs`
- `Features/Search/SearchService.cs`
- `Features/Animations/AnimationService.cs`
- `Features/Animations/LoadingAnimations.cs`
- `Features/Waveform/WaveformRenderer.cs`

## Bounded Slice

Integrate the nine capabilities into canonical runtime paths without resurrecting duplicate legacy architecture.

### Allowlist

| Action | Target |
|--------|--------|
| Create/Edit | `src/VoiceStudio.App/Services/*` for canonical service gaps (search, notifications, animation) |
| Create/Edit | `src/VoiceStudio.App/ViewModels/*` for canonical toolbar/search integration |
| Create/Edit | `src/VoiceStudio.App/Controls/*` only where required for canonical bindings or waveform/animation parity |
| Create/Edit | `src/VoiceStudio.Core/*` only for stable contracts/interfaces required by canonical integration |
| Edit | `src/VoiceStudio.App/MainWindow.xaml.cs`, `src/VoiceStudio.App/Services/AppServices.cs` for composition-root wiring only |
| Create/Edit | `src/VoiceStudio.App.Tests/*` for integration/unit coverage of newly represented capabilities |
| Edit | Governance closure surfaces (tracker, registry, STATE, closure report) |

### Hard OUT

- Do not restore confirmed duplicate files/services as active parallel architecture.
- No backend Python route/service changes.
- No engine-layer runtime changes.
- No semantic expansion beyond preserving the nine capabilities in canonical paths.
- No sidecar dead code additions disconnected from composition root.

## Acceptance Contract

- [x] New row frozen and referenced in tracker/state.
- [x] `ToolbarViewModel` capability represented in canonical shell composition path.
- [x] `PanelManager` capability map verified against canonical decomposition (`PanelRegistry` + `PanelStateService` + `PanelHost` + navigation coordinator), with gaps filled if any.
- [x] Keyboard shortcut + command palette capabilities verified/filled in canonical path.
- [x] Search capability includes canonical local/provider support (not backend-only failure mode).
- [x] Notification center capability represented (history/unread/actionable lifecycle), integrated with toast surface.
- [x] Accessibility capability coverage verified against legacy feature intent; gaps filled canonically.
- [x] Animation capability represented via canonical service abstraction with reduced-motion awareness.
- [x] Loading animation intent represented in canonical controls/service.
- [x] Waveform renderer capability coverage verified and filled where missing.
- [x] All new/updated capabilities wired through composition root and real shell/panel/navigation paths.
- [x] Verification set passes: build, tests, `pytest tests/ci`, `.\scripts\verify.ps1 -Quick`, `run_verification.py` completion guard.

## Rollback

Revert this lane commit(s) to return to pre-integration delete-only GAP-005 state.

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Reintroducing parallel architecture while restoring capabilities | Medium | High | Re-home capabilities into canonical services/ViewModels only; no `Features/` resurrection |
| Toolbar MVVM re-homing causes shell regressions | Medium | High | Preserve existing behavior-first path with incremental binding and tests |
| Notification center introduces state sprawl | Medium | Medium | Define tight contract, bounded retention, and explicit unread lifecycle tests |
| Search provider aggregation harms responsiveness | Low | Medium | Timeout budget + cancellation + provider prioritization |
| Animation/waveform parity changes impact rendering stability | Low | Medium | Keep existing controls as authority; add capability deltas only with tests |

## Changelog

| Date | Entry |
|------|-------|
| 2026-04-08 | Row created and scope frozen per corrective directive superseding GAP-005 delete-only closure |
| 2026-04-08 | **Closed** - canonical integration completed for toolbar/search/notifications/animation plus panel/keyboard/accessibility/loading/waveform coverage validation; proofs: `dotnet build` PASS, App.Tests **3206** passed (**274** skipped), `pytest tests/ci` **217** passed (**2** deselected), Quick `artifacts/verify/20260408_082154/` PASS, rolling `python scripts/run_verification.py` PASS |
