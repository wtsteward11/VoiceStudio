# GOV-VOICESTUDIO-GAP050-EMOTION-PRESET-PROSODY-MAPPING-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01`  
**Status:** **Closed** (closure 2026-04-06)  
**Tracker:** [GAP-050](PROFESSIONAL_GAP_TRACKER.md) — bounded slice only (not full GAP-050 umbrella)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** [GAP-023](PROFESSIONAL_GAP_TRACKER.md) — `ProsodyAuthorityService.apply_transform`

## Problem statement

`POST /api/emotion/apply-extended` duplicated prosody DSP (librosa pitch/time/formant) instead of delegating to the prosody authority. Canonical **emotion preset → pitch/rate/volume** semantics were not table-driven or testable. WinUI discarded structured diagnostics.

## Frozen architecture decisions

1. **DSP owner:** [backend/services/prosody_authority_service.py](../../backend/services/prosody_authority_service.py) — **only** `apply_transform` for pitch/rate/volume on this lane.
2. **Mapping owner:** [backend/services/emotion_preset_prosody_mapper.py](../../backend/services/emotion_preset_prosody_mapper.py) — deterministic preset/emotion → factors; clamped; unit-tested.
3. **Orchestration:** [backend/api/routes/emotion.py](../../backend/api/routes/emotion.py) `POST /api/emotion/apply-extended` — load audio, map, delegate transform, artifact spine; **no** route-local librosa pitch/time.
4. **Diagnostics:** [backend/api/models_additional.py](../../backend/api/models_additional.py) — `ProsodyHandlingDiagnostics` on `EmotionApplyExtendedResponse` (reuse GAP-023 model).
5. **UI seam:** [IEmotionControlClient.cs](../../src/VoiceStudio.App/Core/Services/IEmotionControlClient.cs) / [EmotionControlClient.cs](../../src/VoiceStudio.App/Services/EmotionControlClient.cs) / [EmotionControlViewModel.cs](../../src/VoiceStudio.App/ViewModels/EmotionControlViewModel.cs) — typed response DTO; **no** new `IBackendClient` usage in ViewModels.

## §0.1 Canonical preset mapping (MVP lane)

| Preset (case-insensitive) | Role |
|---------------------------|------|
| Neutral | Identity center |
| Warm | Mild warmth (bounded pitch/rate/volume deltas) |
| Energetic | Higher energy |
| Calm | Lower arousal |

Legacy entries in `AVAILABLE_EMOTIONS` (happy, sad, …) map through the **legacy delta table** (same numeric intent as pre-lane route) but **formant** is not executed — recorded as skipped + warning.

## Contract (chosen)

- **Factors:** `pitch`, `rate`, `volume` multipliers as consumed by `apply_transform` (see GAP-023).
- **Intensity:** Primary/secondary intensities (0–100) scale emotion deltas from identity before blending (matches prior blend semantics).
- **Honesty:** Missing audio → **404**; missing soundfile for load → **503**; requested transform (non-identity factors) and authority/deps failure → **503**/**500** per `ProsodyAuthorityError`. Identity mapping → **200** with `prosody_handling.action == "none"` and new artifact (copy).
- **`timeline_curve`:** Not applied in this slice; optional warning in `prosody_handling.warnings` when present.

## HTTP policy

| Condition | Status |
|-----------|--------|
| Unknown `primary_emotion` / `secondary_emotion` | 400 |
| Audio missing | 404 |
| soundfile missing | 503 |
| Authority / DSP failure | 503 / 500 |

## Acceptance contract (Close)

- [x] `emotion_preset_prosody_mapper` is the single mapping seam for this lane; deterministic tests.
- [x] `apply-extended` uses **only** `apply_transform` for DSP (no duplicate librosa pitch/time in route).
- [x] Response includes `prosody_handling` + `emotion_mapping_source`.
- [x] WinUI deserializes response and surfaces honest outcomes (no fake success on errors).
- [x] `tests/unit/backend/api/routes/test_emotion.py` module-level skip removed; focused tests run.
- [x] Closure matrix + `run_verification.py` **completion_guard** PASS; tracker + registry + STATE synced.

## Allowlist

`backend/services/emotion_preset_prosody_mapper.py`, `backend/api/routes/emotion.py`, `backend/api/models_additional.py` (response model only if extracted), `src/VoiceStudio.App/Core/Models/EmotionControlModels.cs`, `src/VoiceStudio.App/Core/Services/IEmotionControlClient.cs`, `src/VoiceStudio.App/Services/EmotionControlClient.cs`, `src/VoiceStudio.App/ViewModels/EmotionControlViewModel.cs`, `tests/unit/backend/services/test_emotion_preset_prosody_mapper.py`, `tests/unit/backend/api/routes/test_emotion.py`, `src/VoiceStudio.App.Tests/**/Emotion*`, execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

Startup/shell/workspace, full emotional-AI inference, engine changes, new paid dependencies, `IBackendClient` in Emotion ViewModel, route-local DSP forks.

## Rollback

Revert GAP-050 bounded lane commits only.

## Changelog

- **2026-04-06:** Execution row frozen — bounded preset/prosody mapping lane.
- **2026-04-06:** Lane **Closed** — [VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_LANE_CLOSURE_2026-04-06.md); tracker **GAP-050** umbrella **Open**, bounded slice **Closed**.
