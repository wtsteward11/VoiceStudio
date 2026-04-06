# VOICESTUDIO — GAP-062 Torch venv resolution authority — Lane closure

**Date:** 2026-04-06  
**Execution row:** [GOV_VOICESTUDIO_GAP062_TORCH_VENV_RESOLUTION_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP062_TORCH_VENV_RESOLUTION_AUTHORITY_01_EXECUTION_ROW.md)  
**Tracker:** GAP-062 → **Closed** (this bounded diagnostics lane)

## Summary

Single resolver `backend/services/torch_venv_resolver.py` reports per-`VenvFamily` torch status (`present` | `missing` | `incompatible`) via subprocess `import torch` in each family’s `python.exe` (no torch in FastAPI worker). `resolve_torch_runtime(engine_id)` covers `unresolved` for unmapped engines and non-torch families. `GET /api/settings/torch-venv/effective` (60s cache) exposes `families[]`. `ISettingsClient.GetTorchVenvStatusAsync` added; **no** `IBackendClient` methods. Provisioning script `create_engine_venv.py` documents naming divergence vs `VenvFamilyManager`.

## Verification matrix (required)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| Python unit (new) | `python -m pytest tests/unit/backend/services/test_torch_venv_resolver.py -v` | PASS (8) |
| App.Tests (GAP-062 slice) | `dotnet test ... --filter "FullyQualifiedName~SettingsClientTorchVenvSeamTests\|FullyQualifiedName~IBackendClientTorchVenvBoundaryTests"` | PASS (4) |
| Ruff | `ruff check backend/services/torch_venv_resolver.py tests/unit/backend/services/test_torch_venv_resolver.py` | PASS |
| Quick harness | `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260406_173431/`) |
| Rolling verification | `python scripts/run_verification.py` | PASS (`last_run.json` **20260406-173957**, **completion_guard** PASS) |

## Files touched (allowlist-aligned)

- `backend/services/torch_venv_resolver.py` (new)
- `backend/api/routes/settings.py` (`GET /torch-venv/effective`)
- `src/VoiceStudio.App/Core/Models/TorchVenvStatusResponse.cs` (new)
- `src/VoiceStudio.App/Core/Services/ISettingsClient.cs`, `Services/SettingsClient.cs`
- Tests: `test_torch_venv_resolver.py`, `SettingsClientTorchVenvSeamTests.cs`, `IBackendClientTorchVenvBoundaryTests.cs`
- Settings ViewModel test mocks: `GetTorchVenvStatusAsync` default
- `scripts/engines/create_engine_venv.py` (naming documentation block)
- Governance: execution row, this closure, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`

## Rollback

Revert scoped commit(s). Endpoint and client method removal; no UI dependency in this lane.
