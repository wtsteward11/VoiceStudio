# VoiceStudio LUFS Presets Lane Closure — 2026-03-29

**Lane:** GOV-VOICESTUDIO-LUFS-PRESETS-01 (GAP-041)  
**Execution row:** [GOV_VOICESTUDIO_LUFS_PRESETS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_LUFS_PRESETS_01_EXECUTION_ROW.md)

## 1) Scope summary

- **Contract:** `lufs_preset` on `POST /api/timeline/export` (default `podcast_stereo`; `neutral` disables normalization).
- **Backend:** `backend/services/timeline_export_loudness.py` resolves presets; `normalize_lufs` via `backend/audio/audio_utils` after GAP-029 effect bake; **422** invalid preset; **503** when normalization required but LUFS path unavailable (e.g. ImportError from pyloudnorm).
- **Frontend:** Settings default `GeneralSettings.DefaultExportLufsPreset`; optional `IExportLufsPresetUi` per-export picker; `FileOperationsHandler` → `ExportOptions.LufsPreset` → `TimelineUseCase` → API request.

## 2) Verification matrix (mandatory)

| Command | Result (2026-03-29 / hygiene 2026-03-30) |
|--------|------------------------------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable/Roslynator warnings) |
| `dotnet test ... --filter "FullyQualifiedName~FileOperationsHandlerTests|FullyQualifiedName~TimelineUseCaseTests"` | PASS (38) — **targeted** LUFS/export seam regression |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build` (after build) | PASS — **2832 passed**, **274 skipped**, **0 failed** (~2026-03-30 merge-hardening). If **MSB3027** / locked output: terminate stray `testhost.exe`, rebuild, rerun this row. |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/services/test_timeline_export_loudness.py -q` | PASS (39) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (216 selected) |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260329_141103/verification_report.md` (Quick skips sharded C# suites by design) |
| `python scripts/run_verification.py` | PASS — `completion_guard` in `.buildlogs/verification/last_run.json` |

## 3) Proof artifacts

- Route tests: `tests/unit/backend/api/routes/test_timeline.py` — invalid preset 422; neutral skips normalize (monkeypatch); 503 when normalize wrapper raises ImportError; existing `TestExport` requests use `lufs_preset: neutral` so CI does not depend on pyloudnorm for mixdown-only cases.
- Resolver tests: `tests/unit/backend/services/test_timeline_export_loudness.py`.
- Frontend: `FileOperationsHandlerTests` — default `podcast_stereo`, settings neutral, picker override, picker cancel; `TimelineUseCaseTests.ExportAsync_MapsLufsPresetToApiRequest`.

## 4) Honest limits

- Live LUFS metering / true-peak UI remain out of scope (see gap tracker GAP-036).
- Normalization uses existing `normalize_lufs` behavior and environment pyloudnorm availability.

## 5) Closure

**GAP-041:** **Closed** 2026-03-29 with proof-backed acceptance per execution row §6.
