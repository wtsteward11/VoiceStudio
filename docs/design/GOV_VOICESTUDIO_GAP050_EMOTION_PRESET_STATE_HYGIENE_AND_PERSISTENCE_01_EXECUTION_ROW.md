# GOV-VOICESTUDIO-GAP050-EMOTION-PRESET-STATE-HYGIENE-AND-PERSISTENCE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_01`  
**Status:** **Closed** (2026-04-07)  
**Tracker:** [GAP-050](PROFESSIONAL_GAP_TRACKER.md) — product umbrella **Open**  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** [GOV-VOICESTUDIO-GAP050-VOICESYNTHESIS-EMOTION-PRESET-CONSUMER-01](GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01_EXECUTION_ROW.md) — canonical preset consumer + combined capability notice

## Problem statement

Preset authority is wired, but **panel state** for Voice Synthesis can drift across profile changes, panel restore, and repeated operations: emotion preset may survive a profile switch, invalid or legacy preset strings may reappear after restore, and UI/activation could target a different `VoiceSynthesisViewModel` instance than the shell-bound `DataContext` (code-behind `ViewModel` vs `PanelRegistry` DI). Operation-scoped error/status hygiene must not imply a prior run’s capability narrative.

## State ownership (frozen)

| State | Owner | Persisted? |
|-------|--------|------------|
| Canonical emotion preset (`Emotion`) | `VoiceSynthesisViewModel` | **Yes** — workspace/panel state via `IPanelStatePersistable` |
| Selected profile / engine for synthesis | `VoiceSynthesisViewModel` | **Yes** — `SelectedItemId` + `CustomData` |
| SSML/prosody/preset **diagnostics** from last HTTP response | Not stored in VM for UX | **No** — surfaced only as **operation-scoped** toasts |

Not in scope: global user preferences or project JSON for this preset (defer to GAP-070 / product decision).

## Frozen architecture decisions

1. **Single VM instance:** `VoiceSynthesisView` must not construct its own `VoiceSynthesisViewModel`; shell `DataContext` from `PanelRegistry` + `IViewModelFactory` is the only panel VM. Compiled bindings may use `x:DataType` on the **view** type with `{x:Bind ViewModel.…}` where the XAML compiler requires it; `ViewModel` must mirror `DataContext` (no second constructed VM).
2. **Preset validation:** Only the canonical four (`neutral`, `warm`, `energetic`, `calm`), case-insensitive, normalized to lowercase. Any other string → `null` (no silent keep).
3. **Profile switch hygiene:** When `SelectedProfile` changes from one non-null profile to another **distinct** profile id, clear `Emotion` (preset does not carry implicitly across voices). Clearing `SelectedProfile` to null still clears `Emotion`.
4. **Restore hygiene:** `RestoreStateAsync` applies saved profile/engine/preset only when valid; invalid saved preset is dropped. Pending restore coordinates with async `LoadProfilesAsync` via a small pending-restore buffer.
5. **Operation-scoped narrative:** Starting batch synthesis, streaming, or ensemble clears prior operation error flags where applicable so a later success is not framed by stale `HasError` / `ErrorMessage` from an earlier attempt.
6. **Authority unchanged:** No new `IBackendClient` on `VoiceSynthesisViewModel`; no local DSP for presets; `VoiceSynthesisService` orchestration unchanged.

## Acceptance contract (Close)

- [x] Preset selection authority remains single-path (ViewModel → request → `VoiceSynthesisService`); no second preset authority in the panel.
- [x] Invalid preset cannot survive profile clear, profile id change, or restore (normalized or nulled).
- [x] Restored preset is either a valid canonical value or explicitly `null`.
- [x] No stale SSML/prosody/preset **message text** stored on the VM; capability notices remain toast-scoped per operation.
- [x] No new `IBackendClient` surface on `VoiceSynthesisViewModel`.
- [x] `VoiceSynthesisView` + `PanelHost` use the same `VoiceSynthesisViewModel` instance as `DataContext`.
- [x] Tests: transitions (profile switch, restore valid/invalid), back-to-back synthesis warning counts, failure-then-success cleanup, panel state round-trip or restore unit tests.
- [x] Closure matrix: `dotnet build`, App.Tests (full if runtime path touched), `pytest tests/ci`, `validate_xaml_resources.py`, `verify.ps1 -Quick`, `run_verification.py` (**completion_guard** PASS); tracker + registry + STATE synced.

## Allowlist

`src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs`, `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml`, `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml.cs`, `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs`, new/updated tests under `src/VoiceStudio.App.Tests/Panels/` for persistence, execution row, lane closure report, [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md), [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md), [.cursor/STATE.md](../../.cursor/STATE.md).

## Hard OUT

New emotion ML, second DSP path, streaming protocol redesign, shell/startup changes, broad Voice Synthesis UX redesign, mixing in pytest import-shadow remediation (separate proof-hardening row if needed).

## Rollback

Revert this lane’s commits; preserve closed GAP-050 mapping + consumer lanes and GAP-023 authority.

## Changelog

- **2026-04-07:** Row **Closed** — [VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_LANE_CLOSURE_2026-04-07.md); Quick `artifacts/verify/20260407_185825/`; rolling **20260407-190416**; full App.Tests **3193** passed / **274** skipped; `pytest tests/ci` **217**; commit `COMMIT_HASH_PLACEHOLDER`.
- **2026-04-06:** Row **Frozen** — state hygiene + persistence + single-VM binding.
