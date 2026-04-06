# VOICESTUDIO-GAP054-SSML-CAPABILITY-DETECT-STRIP-WARN — Lane closure (2026-04-06)

**Lane:** `GOV_VOICESTUDIO_GAP054_SSML_CAPABILITY_DETECT_STRIP_WARN_01`  
**Tracker:** [GAP-054](../../design/PROFESSIONAL_GAP_TRACKER.md) **Closed**  
**Execution row:** [GOV_VOICESTUDIO_GAP054_SSML_CAPABILITY_DETECT_STRIP_WARN_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP054_SSML_CAPABILITY_DETECT_STRIP_WARN_01_EXECUTION_ROW.md)

## 1. Outcome

Single backend SSML policy authority (`backend/services/ssml_capability_resolver.py`) applied on `SynthesisService.synthesize` and mirrored on `POST /api/voice/synthesize` before NLP preprocess. Success responses may include `ssml_handling` (Pydantic `SsmlHandlingDiagnostics`). Malformed SSML → **422** (`ServiceError` / `HTTPException`). SSML preview uses raw content through canonical synthesis; response includes optional `ssml_handling` dict. C# `VoiceSynthesisResponse.SsmlHandling` + boundary test (no `IBackendClient` SSML creep).

## 2. Verification matrix (required)

| Step | Command | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable warnings only) |
| Py — resolver + preview | `python -m pytest tests/unit/backend/services/test_ssml_capability_resolver.py tests/unit/backend/api/routes/test_ssml_gap054_preview.py -v` | PASS (**14**) |
| Py — CI slice | `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (**217** selected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_180531/` |
| Rolling verification | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **20260406-181111** (**completion_guard** PASS) |

## 3. Proof pointers

- App.Tests filter: `FullyQualifiedName~VoiceSynthesisSsmlDiagnosticsTests|FullyQualifiedName~IBackendClientSsmlBoundaryTests` — **2** PASS  
- New artifacts: `src/VoiceStudio.App/Core/Models/SsmlHandlingDiagnostics.cs`, tests under `src/VoiceStudio.App.Tests/Core/` and `Services/`

## 4. Rollback

Revert scoped commit(s). Clients ignore unknown JSON fields; omission of `ssml_handling` restores prior behavior.
