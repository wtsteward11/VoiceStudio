# GOV-VOICESTUDIO-GAP023-PROSODY-AUTHORITY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP023_PROSODY_AUTHORITY_01`  
**Status:** **Closed** (closure 2026-04-07)  
**Tracker:** [GAP-023](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** None (isolated backend prosody + WinUI contract seam)

## Problem statement

Prosody paths were inconsistent: `/api/voice/prosody-control` returned **501** while `/api/prosody/apply` could **silently skip** pitch/rate DSP on failure and still return success. Response shapes drifted from the WinUI `ProsodyApplyResponse` DTO. GAP-023 requires one canonical transform authority and honest outcomes.

## Frozen architecture decisions

1. **Canonical prosody DSP owner:** [backend/services/prosody_authority_service.py](../../backend/services/prosody_authority_service.py) — numpy in/out; uses [backend/audio/audio_utils.py](../../backend/audio/audio_utils.py) (`pitch_shift_audio`, `time_stretch_audio`); no route-local duplicate policy.
2. **Orchestration:** [backend/api/routes/prosody.py](../../backend/api/routes/prosody.py) `POST /api/prosody/apply` — synthesis via `SynthesisService`, then authority transform + artifact spine.
3. **Voice quality route:** [backend/api/routes/voice/processing.py](../../backend/api/routes/voice/processing.py) `POST /api/voice/prosody-control` — load audio by `audio_id`, delegate transform, return `ProsodyControlResponse`.
4. **Transport models:** [backend/api/models_additional.py](../../backend/api/models_additional.py) — `ProsodyHandlingDiagnostics`, extended apply response fields as needed.
5. **UI seam:** [IProsodyClient.cs](../../src/VoiceStudio.App/Core/Services/IProsodyClient.cs) / [ProsodyClient.cs](../../src/VoiceStudio.App/Services/ProsodyClient.cs) / [ProsodyViewModel.cs](../../src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs) — DTOs aligned with backend JSON (snake_case).

## §0.1 Authority map

| Surface | Role | Canonical? |
|--------|------|------------|
| `ProsodyAuthorityService.apply_transform` | Pitch/rate/volume on `np.ndarray` | **Yes** |
| `POST /api/prosody/apply` | Synthesis + transform + artifact | **Yes** (orchestrator) |
| `POST /api/voice/prosody-control` | Load WAV + transform + artifact | **Yes** (orchestrator) |
| Route-local `try/except` swallow on pitch/rate | — | **No** (forbidden post-lane) |

## Contract (chosen)

- **Pitch multiplier** (UI config style): `1.0` = no change; `semitones = 12 * (pitch - 1.0)` (matches prior `prosody.py` mapping).
- **Rate:** `time_stretch_audio(..., rate=rate, preserve_pitch=True)` when `rate != 1.0`.
- **Volume:** multiply + peak normalize if needed.
- **Honesty:** If pitch or rate transform is **requested** (`!= 1.0`) and DSP **cannot** run (missing deps or runtime error), the request **fails** with HTTP **503** (deps) or **500** (DSP error) — **no** silent skip.
- **No-op:** All factors at identity → output equals input (copy), `prosody_handling` records `action: "none"`.
- **`/api/voice/prosody-control`:** `pitch_contour` → use **mean** as multiplier when list non-empty; `rhythm_adjustments` → `rate` or `tempo` key. **Unsupported-only** requests (`stress_markers` / `prosody_template` / `intonation_pattern` without numeric transform) → **422** with explicit detail.

## HTTP policy

| Condition | Status |
|-----------|--------|
| Missing audio / empty file | 404 / 400 |
| Librosa/soundfile missing (load path) | 503 |
| Pitch or rate requested, transform deps fail | 503 |
| Pitch or rate requested, transform raises | 500 |
| Unsupported prosody-control payload | 422 |

## Acceptance contract (all required for Close)

- [x] `ProsodyAuthorityService` is sole DSP decision path for pitch/rate/volume in this lane.
- [x] `/api/prosody/apply` never returns success after silently skipping requested pitch/rate.
- [x] `/api/voice/prosody-control` implements bounded DSP (contour + rhythm keys) or returns 422; no fake 501 for implemented path.
- [x] `ProsodyApplyResponse` C# model deserializes backend JSON; `audio_url` uses `/api/voice/audio/{id}`.
- [x] Tests: service unit tests, updated stub honesty test, App seam / deserialization test.
- [x] Closure matrix + `run_verification.py` **completion_guard** PASS; tracker + registry + STATE synced.

## Allowlist

`backend/services/prosody_authority_service.py`, `backend/api/routes/prosody.py`, `backend/api/routes/voice/processing.py`, `backend/api/models_additional.py`, `src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs`, `src/VoiceStudio.App.Tests/**/Prosody*`, `tests/unit/backend/services/test_prosody_authority_service.py`, `tests/unit/backend/api/routes/test_prosody_stub_honesty.py`, execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

Startup orchestration, shell/workspace (GAP-070), broad synthesis refactor, new `IBackendClient` methods, engine manifest redesign, SSML policy changes beyond consumer of diagnostics shape.

## Rollback

Revert GAP-023 scoped commits only.

## Changelog

- **2026-04-07:** Execution row frozen; lane **Closed** — [VOICESTUDIO_GAP023_PROSODY_AUTHORITY_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP023_PROSODY_AUTHORITY_LANE_CLOSURE_2026-04-07.md); `ProsodyAuthorityService`, `/api/prosody/apply` + `/api/voice/prosody-control` delegation, `ProsodyHandlingDiagnostics`, WinUI contract tests.
