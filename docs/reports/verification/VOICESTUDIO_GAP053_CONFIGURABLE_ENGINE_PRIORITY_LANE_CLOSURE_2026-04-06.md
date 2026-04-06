# VOICESTUDIO — GAP-053 Configurable engine priority — Lane closure

**Date:** 2026-04-06  
**Execution row:** [GOV_VOICESTUDIO_GAP053_CONFIGURABLE_ENGINE_PRIORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP053_CONFIGURABLE_ENGINE_PRIORITY_01_EXECUTION_ROW.md)  
**Tracker:** GAP-053 → **Closed** (this bounded lane)

## Summary

Implemented user-configurable TTS engine fallback priority with a single resolver (`backend/services/engine_priority.py`), persistence on `EngineSettings.engine_priority_order`, synthesis + router consumption, diagnostics endpoint `GET /api/settings/engine-priority/effective`, and minimal Settings UI (list + move up/down + reset). `IBackendClient` was not extended; effective priority is fetched via `ISettingsClient`.

## Verification matrix (required)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in other files) |
| App.Tests (GAP-053 slice) | `dotnet test ... --filter "FullyQualifiedName~SettingsViewModelEnginePriorityTests|FullyQualifiedName~IBackendClientEnginePriorityBoundaryTests"` | PASS (8) |
| Python unit (new) | `python -m pytest tests/unit/backend/services/test_engine_priority.py -v` | PASS (7) |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (217, 2 deselected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Rolling verification | `python scripts/run_verification.py` | PASS (`last_run.json` **20260406-170806**, **completion_guard** PASS) |
| Quick harness | `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260406_172223/`; report `verification_report.md`; build + Python quality + Quick Critical Gates + Security + Gate/Ledger) |

## Files touched (allowlist-aligned)

- `backend/services/engine_priority.py` (new)
- `backend/api/routes/settings.py`
- `backend/services/synthesis_service.py`
- `app/core/engines/router.py`
- `src/VoiceStudio.App/Core/Models/SettingsData.cs` (EnginePriorityOrder)
- `src/VoiceStudio.App/Core/Models/EffectiveEnginePriorityResponse.cs` (new)
- `src/VoiceStudio.App/Core/Services/ISettingsClient.cs`, `Services/SettingsClient.cs`
- `src/VoiceStudio.App/ViewModels/SettingsViewModel.cs`, `Views/Panels/SettingsView.xaml`, `SettingsView.xaml.cs`
- `src/VoiceStudio.App/Services/SettingsService.cs`
- Tests: `tests/unit/backend/services/test_engine_priority.py`, `SettingsViewModelEnginePriorityTests.cs`, `IBackendClientEnginePriorityBoundaryTests.cs`, seam test mock updates

## Rollback

Revert scoped commit(s). Empty `engine_priority_order` restores YAML → default behavior.
