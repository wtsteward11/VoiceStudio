# GOV-VOICESTUDIO-LUFS-PRESETS-01 — Execution Row (GAP-041)

**Status:** **Closed** — GAP-041; closure [VOICESTUDIO_LUFS_PRESETS_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_LUFS_PRESETS_LANE_CLOSURE_2026-03-29.md).  
**Date:** 2026-03-29  
**Depends on:** [GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md) **Closed** (GAP-029).

## 1) Authority inventory

| Concern | Current owner (pre-lane) | Canonical owner (post-lane) |
|--------|---------------------------|-----------------------------|
| Export loudness target | Implicit peak scaling in `_write_audio_output` only; C# `ExportOptions.TargetLufs` / `NormalizeLoudness` unused on wire | **`lufs_preset` on `POST /api/timeline/export`** resolved to target LUFS (or off) in `backend/services/timeline_export_loudness.py` |
| Preset definitions | Scattered hints (orchestrator JSON, mastering rack); no export contract | **Frozen table in execution row §2 + resolver module** |
| Normalization processing | `app/core/audio/audio_utils.normalize_lufs` (not on timeline export); `/api/audio/export` optional bool only | **`normalize_lufs` via `backend/audio/audio_utils` facade**, applied in `export_timeline` after effect bake |
| Preset selection UI/source | None for export | **Settings:** `GeneralSettings.DefaultExportLufsPreset`; **export flow:** optional `ShowContentAsync` picker overriding default for one export |
| Validation / fallback | N/A | **422** unknown preset; **503** when LUFS normalization requested but pyloudnorm unavailable; **neutral** = explicit no normalization |

## 2) Frozen preset table (binary)

| `lufs_preset` id | Label | Target integrated LUFS | Normalize |
|------------------|-------|-------------------------|-----------|
| `podcast_stereo` | Podcast (stereo) | -16.0 | yes |
| `podcast_mono` | Podcast (mono) | -19.0 | yes |
| `broadcast` | Broadcast (EBU-style target) | -23.0 | yes |
| `streaming` | Streaming / platform loud | -14.0 | yes |
| `neutral` | Neutral / none | — | no |

**Default (API + product):** `podcast_stereo` unless client sends `neutral`.

## 3) Hard IN scope

- Preset table + resolver + canonical `POST /api/timeline/export` contract field `lufs_preset`.
- Backend applies normalization after mixdown and optional GAP-029 effect bake, before file write.
- Hybrid UX: settings default + per-export optional picker on file export command path.
- Tests + closure report + gap tracker + STATE + registry.

## 4) Hard OUT of scope

- Live metering, true-peak UI, waveform edit, mastering redesign, PanelHost, `/api/audio/export` as project export path.

## 5) Contract freeze

**`ExportRequest` (subset):**

- `lufs_preset: str` — one of §2 ids; default `podcast_stereo`.

**Processing order:** render → fallback audio if needed → **effect bake if `apply_effects`** → **LUFS preset** → `_write_audio_output`.

## 6) Binary acceptance

1. **AC1:** File-menu export uses `ITimelineUseCase` only; payload includes `lufs_preset`; never `IBackendClient.ExportAudioAsync` for that flow.
2. **AC2:** Non-`neutral` preset applies `normalize_lufs` with correct target (covered by route tests + resolver unit tests).
3. **AC3:** Unknown `lufs_preset` → **422**.
4. **AC4:** `neutral` → no normalization stage (test-covered).
5. **AC5:** pyloudnorm unavailable when normalization required → **503** (test via patch).
6. **AC6:** Full gates green + `completion_guard` PASS; GAP-041 closed only with proof artifacts. **Merge-hardening (post-closure):** full **unfiltered** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` recorded in [VOICESTUDIO_LUFS_PRESETS_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_LUFS_PRESETS_LANE_CLOSURE_2026-03-29.md) §2; the **38-test filter** remains a valid **supplemental** targeted check for LUFS/export seams, not a substitute for that full-suite row.

## 7) Rollback

Revert: timeline route + `timeline_export_loudness` + facade export + frontend preset wiring + settings field. Do not revert GAP029 effect bake.
