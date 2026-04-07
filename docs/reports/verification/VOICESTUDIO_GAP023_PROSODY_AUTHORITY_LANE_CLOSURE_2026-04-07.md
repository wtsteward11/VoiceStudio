# VOICESTUDIO — GAP-023 Prosody Authority Lane Closure

**Date:** 2026-04-07  
**Lane:** `GOV-VOICESTUDIO-GAP023-PROSODY-AUTHORITY-01`  
**Execution row:** [GOV_VOICESTUDIO_GAP023_PROSODY_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP023_PROSODY_AUTHORITY_01_EXECUTION_ROW.md)  
**Tracker:** [GAP-023](../../design/PROFESSIONAL_GAP_TRACKER.md) → **Closed**

## 1. Goal (recap)

Single canonical backend authority for pitch/rate/volume prosody transforms; remove silent skip on `/api/prosody/apply`; replace `/api/voice/prosody-control` **501** with bounded real DSP + honest **422** for unsupported-only payloads; align WinUI `ProsodyApplyResponse` with backend JSON.

## 2. Code-truth summary

| Area | Change |
|------|--------|
| Authority | New [backend/services/prosody_authority_service.py](../../../backend/services/prosody_authority_service.py) — `apply_transform`, `ProsodyAuthorityError`, `prosody_control_request_factors` |
| Apply route | [backend/api/routes/prosody.py](../../../backend/api/routes/prosody.py) delegates post-synth DSP to authority; returns `ProsodyApplyResponseModel` + `/api/voice/audio/{id}` |
| Voice route | [backend/api/routes/voice/processing.py](../../../backend/api/routes/voice/processing.py) `prosody_control` loads via `load_audio`, delegates to authority, artifact spine |
| Models | [backend/api/models_additional.py](../../../backend/api/models_additional.py) — `ProsodyHandlingDiagnostics`, `ProsodyApplyResponseModel` |
| WinUI | [ProsodyViewModel.cs](../../../src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs) — `ProsodyApplyResponse`, `ProsodyHandlingDiagnostics`; status line uses audio id + DSP suffix |

## 3. Verification matrix (lane)

| Step | Command / artifact | Result |
|------|-------------------|--------|
| Prosody pytest | `python -m pytest tests/unit/backend/services/test_prosody_authority_service.py tests/unit/backend/api/routes/test_prosody_stub_honesty.py tests/unit/backend/api/routes/test_prosody.py -q` | **10** passed |
| CI slice | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260407_013855/` |
| Rolling verify | `python scripts/run_verification.py` (post-commit clean tree) | PASS — `.buildlogs/verification/last_run.json` **timestamp_short** **20260407-014814** (**completion_guard** PASS) |
| C# Prosody slice | `dotnet test ... --filter "FullyQualifiedName~Prosody"` | **5** passed, **1** skipped (`Panel_Prosody_CanNavigate`) |

## 4. Honesty / anti-relapse

- `test_prosody_stub_honesty.py` asserts **200** when DSP path is mocked (no fake **501** for contour requests); **422** for metadata-only and empty-transform requests.
- Service tests assert **503** on `ImportError` and **500** on runtime DSP failure when pitch is requested.

## 5. Out of scope (per execution row)

Per-row **Hard OUT**: startup, shell/workspace (GAP-070), broad synthesis redesign, `IBackendClient` surface expansion, engine manifest redesign.

## 6. Rollback

Revert commits scoped to GAP-023 files listed in execution row allowlist.
