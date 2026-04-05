# GOV-VOICESTUDIO-EXPORT-AUTHORITY-01 — Execution Row (GAP-029)

**Status:** **Closed** — 2026-03-29 — [VOICESTUDIO_EXPORT_AUTHORITY_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_EXPORT_AUTHORITY_LANE_CLOSURE_2026-03-29.md)  
**Date:** 2026-03-29  
**Depends on:** GAP-020 Session Autosave merge-hardened; closed foundation lanes remain closed.

## 1) Authority inventory (frozen)

| Concern | Owner | Canonical surface |
|--------|--------|-------------------|
| File menu “Export Audio” (project open) | `FileOperationsHandler.ExportAudioAsync` | **Must** call `ITimelineUseCase.ExportAsync` → `POST /api/timeline/export` only (no direct `/api/audio/export` for this flow). |
| Timeline panel / use-case export | `TimelineUseCase.ExportAsync` | Same endpoint; request body matches `ExportRequest` (snake_case JSON). |
| Mixdown / render | `backend/api/routes/timeline.py` | `_render_timeline_audio` + `_write_audio_output`. |
| Effect bake on export | `backend/api/routes/timeline.py` + `backend/services/timeline_effect_bake.py` | When `apply_effects=true` and `effect_chain_id` set, chain is applied to mixdown **before** format write. |
| Standalone asset transcode | `backend/api/routes/audio.py` `POST /api/audio/export` | **Out of scope** for this lane except library/diagnostic callers; not used for project/timeline export. |

## 2) Hard IN scope

- Timeline / project audio export uses **one** HTTP route: `POST /api/timeline/export`.
- Export may include **active** effect chain when client sends `apply_effects` + `effect_chain_id` + `project_id`.
- **Selection authority:** `IContextManager.ActiveEffectChainId` reflects Effects Mixer selection for export bake.
- **Fallback:** When timeline has no mixable audio, client may pass `fallback_project_audio_id` so export still produces output via the **same** route.

## 3) Hard OUT of scope

- Video/dataset/model export redesign.
- Plugin/collab/meter/waveform features.
- Replacing `/api/audio/export` for non-menu library flows.
- PanelHost / GAP-007.

## 4) Contract freeze (binary)

**Request (`ExportRequest`):**

- `output_path` (required, absolute path preferred; server may relocate unsafe paths).
- `format` (default `wav`).
- `sample_rate` (optional; default timeline SR).
- `project_id` (required if `apply_effects` is true).
- `apply_effects` (bool, default false).
- `effect_chain_id` (optional; required when `apply_effects` is true).
- `fallback_project_audio_id` (optional; backend audio id for mixdown fallback).

**Validation:**

- `apply_effects=true` without `effect_chain_id` or `project_id` → **422**.
- `apply_effects=true` with unknown chain / wrong project / no enabled effects → **400** (no silent success as effect-free export).

**Response:** `ExportResponse` — `success`, `output_path`, `duration`.

## 5) Binary acceptance criteria

1. **AC1:** `FileOperationsHandler` never calls `IBackendClient.ExportAudioAsync` for the file.export command path when backend is available and a project is open (uses timeline export only).
2. **AC2:** With `apply_effects=true` and a valid chain, exported file reflects processing (integration test asserts chain invoked / distinct from dry mix where applicable).
3. **AC3:** With `apply_effects=true` and invalid chain, API returns non-2xx — **no** effect-blind success.
4. **AC4:** `pytest` + `dotnet test` + `verify.ps1 -Quick` + `run_verification.py` PASS on closure commit.

## 6) Rollback

Revert order: backend effect bake → frontend handler/use case → lane doc. Autosave and other closed lanes untouched.
