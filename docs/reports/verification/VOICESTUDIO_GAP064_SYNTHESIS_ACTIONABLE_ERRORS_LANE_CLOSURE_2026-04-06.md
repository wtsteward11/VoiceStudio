# GAP-064 — Synthesis actionable errors lane closure

**Lane:** `GOV_VOICESTUDIO_GAP064_SYNTHESIS_ACTIONABLE_ERRORS_01`  
**Tracker:** GAP-064 **Closed** (bounded UX slice; parent tracker narrative may list related follow-ups)  
**Date:** 2026-04-06

## 1. Scope delivered

- `ActionableErrorTranslator` + `ActionableErrorInfo` / enums (`VoiceStudio.Core.Models` in App/Core/Models).
- `ErrorHandler` delegates user primary + recovery copy through the translator (`General` context).
- `ErrorPresentationService` toast titles use translator output.
- `VoiceSynthesisService`: `BackendNotFoundException` propagates; `HttpRequestException` → `BackendUnavailableException` without appending raw socket text.
- `VoiceSynthesisViewModel`: synthesis errors use translator (`VoiceSynthesize`); success shows warning when `ssml_handling.action == stripped_warned`.
- `SSMLControlViewModel`: preview/validate/update/delete errors use translator; preview success warns on `stripped_warned`.
- `SSMLPreviewResult.ssml_handling` JSON pass-through on `ISSMLClient` / `SSMLClient` (DTO only).
- Removed duplicate synthesis error/success toasts from `VoiceSynthesisView.xaml.cs` (VM owns narrative).

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable warnings only) |
| Targeted MSTest | `dotnet test ... --filter "FullyQualifiedName~ActionableErrorTranslatorTests\|...\|SSMLControlViewModelTests"` | **20** PASS |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_183221/` |
| Ledger / guard | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **20260406-183731** (**completion_guard** PASS) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260406_183221/`
- Verification JSON: `.buildlogs/verification/last_run.json` (`timestamp_short`: **20260406-183731**)

## 4. Rollback

Revert the GAP-064 commit(s). No backend contract changes.
