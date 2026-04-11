# GAP-063 — First-run wizard (models, GPU, keys, telemetry) lane closure

**Lane:** `GOV-VOICESTUDIO-GAP063-FIRST-RUN-WIZARD-MODELS-GPU-KEYS-01`  
**Tracker:** GAP-063 **Closed**  
**Date:** 2026-04-09  

## 1. Scope delivered

- **5-step wizard:** Welcome → System check (GPU CPU-fallback panel) → Model readiness (`IModelManagerClient.GetModelsAsync` + informational CTA) → Backend health (`IDiagnosticsClient.CheckHealthAsync` + `IEnginesClient.GetEnginesAsync`) → API keys orientation + finish (`DontShowAgain`).
- **Backend start:** `BackendProcessManager.EnsureBackendRunningAsync` (no raw uvicorn `Process.Start`).
- **Telemetry:** `TelemetryConsentDialog` once, from `WizardRootGrid` **Loaded**, key `TelemetryConsentShown`; XamlRoot set (ADR-047).
- **DI:** `OnboardingWizardService` singleton + `AppServices.GetOnboardingWizardService()`; `GetProgress().CurrentStepId` synced to `first_run_step_{n}`.
- **Persistence:** `WizardCurrentStep` in `UnpackagedSettingsHelper`; reset to `1` on completion; `public const WizardCurrentStepKey`.
- **Exit on cancel:** `FirstRunWizard(isFirstRun)`; `App.xaml.cs` exits only when `!wizard.WasCompleted && isFirstRun` (repeat-show wizard close does not exit).
- **AutomationIds:** 10 entries in `docs/developer/AUTOMATION_ID_REGISTRY.md` (FirstRunWizard_*).
- **Tests:** `FirstRunWizardTests` **10** (settings contract, source seams, DI registration scan).

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| GAP-063 targeted | `--filter FullyQualifiedName~FirstRunWizard` | **10** PASS |
| Full App.Tests | `dotnet test src/VoiceStudio.App.Tests/...` | **3233** PASS / **274** skipped |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260409_190351/` |
| Rolling harness | `python scripts/run_verification.py` | `.buildlogs/verification/last_run.json` **20260409-190848** (Overall PASS) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260409_190351/`
- Verification JSON: `.buildlogs/verification/last_run.json` (`timestamp_short`: **20260409-190848**)
- Execution row (Closed): `docs/design/GOV_VOICESTUDIO_GAP063_FIRST_RUN_WIZARD_MODELS_GPU_KEYS_01_EXECUTION_ROW.md`

## 4. Rollback

Revert GAP-063 commits: `FirstRunWizard` XAML/code-behind, `App.xaml.cs` wizard gate, `AppServices` registration, tests, registry/automation docs.
