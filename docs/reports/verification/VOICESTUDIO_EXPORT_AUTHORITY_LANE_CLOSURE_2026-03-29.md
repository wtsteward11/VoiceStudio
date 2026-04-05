# VoiceStudio Export Authority Lane Closure — 2026-03-29

**Lane:** GOV-VOICESTUDIO-EXPORT-AUTHORITY-01 (GAP-029)  
**Execution row:** [GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md)

## 1) Scope summary

- **Canonical route:** `POST /api/timeline/export` for file-menu project audio export; optional `fallback_project_audio_id`; optional effect bake via `apply_effects` + `effect_chain_id` + `project_id`.
- **Frontend:** `FileOperationsHandler.ExportAudioAsync` → `ITimelineUseCase.ExportAsync` (no `/api/audio/export` on this path). Effects Mixer selection → `IContextManager.ActiveEffectChainId`.
- **Backend:** `apply_chain_model_to_audio` shared by effects route and timeline export; `backend/services/timeline_effect_bake.py` validates chain ownership.

## 2) Verification matrix (mandatory)

| Command | Result (2026-03-29) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS (session; includes new `ExportAudio_CallsTimelineExport_NotDirectAudioExport`) |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py -q` | PASS (30) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (earlier session; 216) |
| `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260329_050446/verification_report.md`) |
| `python scripts/run_verification.py` | PASS (`completion_guard` in `last_run.json`) |

## 3) Proof artifacts

- Timeline export tests: `tests/unit/backend/api/routes/test_timeline.py` (`TestExport` effect bake + 422/404).
- Frontend seam: `src/VoiceStudio.App.Tests/Commands/FileOperationsHandlerTests.cs` — `ExportAudio_CallsTimelineExport_NotDirectAudioExport`.
- Use case: `TimelineUseCaseTests.ExportAsync_ReturnsOutputPath` updated for `TimelineExportApiRequest` / `TimelineExportResponseDto`.

## 4) Honest limits

- Timeline mixdown remains in-memory / file-path based; edge cases (missing clip files) log warnings as before.
- Effect bake requires enabled effects in chain; explicit HTTP errors when `apply_effects` is inconsistent (no silent dry success).

## 5) Closure

**GAP-029:** **Closed** 2026-03-29 with proof-backed acceptance per execution row §5.
