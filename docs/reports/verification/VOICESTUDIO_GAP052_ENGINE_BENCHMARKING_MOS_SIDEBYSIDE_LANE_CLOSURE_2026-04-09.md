# GAP-052 Lane Closure Report
## GOV-VOICESTUDIO-GAP052-ENGINE-BENCHMARKING-MOS-SIDEBYSIDE-01

**Date:** 2026-04-09  
**Status:** CLOSED  
**Execution Row:** [GOV_VOICESTUDIO_GAP052_ENGINE_BENCHMARKING_MOS_SIDEBYSIDE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP052_ENGINE_BENCHMARKING_MOS_SIDEBYSIDE_01_EXECUTION_ROW.md)

---

## §1 Summary

Bounded lane **GAP-052** extends the existing **Quality Benchmark** panel (`PanelIds.QualityBenchmark`) with a **side-by-side engine comparison** workflow:

- **Engine list** from `IEnginesClient.GetEnginesAsync` with multi-select (`SelectableComparisonEngineRow`).
- **Per-engine synthesis** via `IVoiceSynthesisService.SynthesizeVoiceAsync` (field `_voiceSynthesisService` so `check_ibackendclient_creep.py` recognizes the seam).
- **Playback** per slot: `IAudioPlayerService.PlayBackendAudioIdAsync` + `BackendClientConfig.BaseUrl`.
- **Objective metrics** from `VoiceSynthesisResponse.QualityMetrics`; **subjective 1–5** slider; **Prefer** via `SetPreferredEngineCommand`.
- **Failure honesty**: `ComparisonSlot.ShouldShowErrorText` + per-slot error without hiding successful slots.
- **Session-light persistence**: `UnpackagedSettingsHelper` keys for comparison engine JSON + comparison test text.
- **Legacy path retained**: `IQualityControlClient.RunBenchmarkAsync` + XTTS/Chatterbox/Tortoise checkboxes unchanged.

**Tests:** `Gap052Tests` (8 source seam scans) + extended `QualityBenchmarkViewModelSeamTests` (constructors, engines load, comparison success/partial failure, play, preference, subjective score).

---

## §2 Acceptance Criteria Matrix

| Criterion | Result |
|-----------|--------|
| ≥2 engines from API, Run comparison | ✅ PASS |
| Horizontal comparison slots, play / score / prefer | ✅ PASS |
| Failed slot shows error; others usable | ✅ PASS |
| Automated Run benchmark unchanged | ✅ PASS |
| `Gap052Tests` (8) + extended seam tests + full App.Tests + `verify.ps1 -Quick` | ✅ PASS |
| AutomationIds `QualityBenchmarkView_RunComparisonButton`, `QualityBenchmarkView_ComparisonSlots` | ✅ PASS |
| Closure + tracker + STATE + CANONICAL_REGISTRY + openmemory | ✅ PASS (this document) |

---

## §3 Files Touched (primary)

- `src/VoiceStudio.App/Views/Panels/QualityBenchmarkViewModel.cs` — GAP-052 VM + `ComparisonSlot` + `_voiceSynthesisService`
- `src/VoiceStudio.App/Views/Panels/QualityBenchmarkView.xaml` — side-by-side section + `x:Name="Self"`
- `src/VoiceStudio.App/Views/Panels/QualityBenchmarkView.xaml.cs` — DI for engines, audio, synthesis, `BackendClientConfig`
- `src/VoiceStudio.App/Services/AppServices.cs` — `GetVoiceSynthesisService()` (if not already present)
- `src/VoiceStudio.App.Tests/Views/Gap052Tests.cs` — NEW
- `src/VoiceStudio.App.Tests/ViewModels/QualityBenchmarkViewModelSeamTests.cs` — extended
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — GAP-052 entries
- `docs/design/GOV_VOICESTUDIO_GAP052_ENGINE_BENCHMARKING_MOS_SIDEBYSIDE_01_EXECUTION_ROW.md` — Closed
- `docs/design/PROFESSIONAL_GAP_TRACKER.md` — GAP-052 Closed

---

## §4 Proof Seal

| Artifact | Value |
|----------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → exit 0 |
| Creep gate | `python scripts/ci/check_ibackendclient_creep.py` → exit 0 (`_voiceSynthesisService.SynthesizeVoiceAsync`) |
| Targeted tests | `Gap052Tests` + `QualityBenchmarkViewModelSeamTests` → 26 PASS |
| Full App.Tests | **3260** PASS / **274** skipped |
| Quick verify | `artifacts/verify/20260409_204411/` PASS |

---

## §5 Hard OUT (confirmed)

- No new panel ID; no removal of `RunBenchmarkAsync`; no backend benchmark API change for closure.
