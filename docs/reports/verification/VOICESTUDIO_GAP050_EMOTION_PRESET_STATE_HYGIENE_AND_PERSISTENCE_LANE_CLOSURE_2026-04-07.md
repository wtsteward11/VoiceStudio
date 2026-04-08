# VOICESTUDIO — GAP-050 bounded slice: Emotion preset state hygiene + panel persistence — Lane closure

**Date:** 2026-04-07  
**Execution row:** [GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_01_EXECUTION_ROW.md)  
**Tracker:** [GAP-050](../../design/PROFESSIONAL_GAP_TRACKER.md) — **product umbrella remains Open**; this document closes the **Voice Synthesis panel state hygiene + `IPanelStatePersistable`** slice only (depends on consumer + mapping lanes + GAP-023).

## 1. Goal

Make Voice Synthesis **emotion preset** and related panel state **deterministic** across profile switches, workspace restore, and repeated synthesis: single shell-bound VM, canonical preset validation, profile-transition clearing, deferred restore coordination with `LoadProfilesAsync`, operation-scoped clearing of error/capability UI flags at synthesis boundaries, and persisted state round-trip without stale SSML/prosody **message text** on the VM.

## 2. Code truth

| Area | Artifact |
|------|-----------|
| ViewModel state machine | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `IPanelStatePersistable`, `NormalizeCanonicalEmotionPreset`, profile-switch hygiene, `BeginSynthesisOperationNarrativeHygiene`, pending restore buffer + `TryCompletePendingPanelRestore` |
| View / bind root | `VoiceSynthesisView.xaml` — `x:DataType` on view + `{x:Bind ViewModel.…}`; `VoiceSynthesisView.xaml.cs` — no duplicate VM construction; `ViewModel` mirrors `DataContext` for compiled bindings |
| Smoke | `MainWindow.Smoke.cs` — `DataContext` / `ViewModel` alignment |
| Response DTO (consumer contract) | `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs` — `VoiceSynthesisResponse.ProsodyHandling`, `EmotionMappingSource`, `EmotionPresetApplyFailureMessage` (+ JSON names) for combined capability diagnostics |
| Tests | `VoiceSynthesisViewModelTests.cs` (transitions, restore valid/invalid, back-to-back warnings, failure→success); `Panels/VoiceSynthesisPanelStatePersistenceTests.cs` (round-trip) |
| Hygiene | `tests/unit/backend/api/routes/test_emotion.py` — removed `noqa` on imports (suppression policy) |

## 3. Verification matrix (lane)

| Step | Command | Result |
|------|---------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (via `verify.ps1 -Quick`) |
| App tests (targeted) | `dotnet test ... --filter "FullyQualifiedName~VoiceSynthesisViewModelTests\|FullyQualifiedName~VoiceSynthesisPanelStatePersistenceTests"` | PASS (**39** passed) |
| Full App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS (**3193** passed / **274** skipped) |
| CI pytest | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260407_185825/` |
| Rolling verifier | `python scripts/run_verification.py` | `.buildlogs/verification/last_run.json` **20260407-190416** — **completion_guard** PASS (post-Quick working tree; see commit hash in STATE after merge). |

### 3.1 Pytest import-shadow quarantine (plan follow-up)

**Policy:** Isolated collection of `tests/unit/backend/api/routes/test_emotion.py` can shadow the repo `backend` package; **authoritative** anti-regression for emotion routes remains **`pytest tests/ci`**. This lane run: **217** CI tests passed — **no import-shadow failure observed**. If isolated shadow recurs in CI or local workflows, open a **separate proof-hardening execution row** (do not mix into GAP-050 runtime lanes).

## 4. Honesty / diagnostics

- Restored custom keys `VoiceSynthesis_EmotionPreset` / `VoiceSynthesis_SelectedEngine` are treated as **untrusted**; non-canonical preset strings become `null`.
- Capability narrative for SSML/prosody/preset remains **toast-scoped** per operation; VM clears `HasError` / `ErrorMessage` / `HasQualityMetrics` at operation start where implemented.

## 5. Hard OUT (unchanged)

Per execution row: no new emotion ML, second DSP path, streaming rewrite, shell/startup architecture changes, broad synthesis UX redesign, mixing pytest import-shadow remediation into this lane.

## 6. Rollback

Revert this lane’s commits only; preserve closed GAP-050 mapping + consumer lanes and GAP-023 authority.
