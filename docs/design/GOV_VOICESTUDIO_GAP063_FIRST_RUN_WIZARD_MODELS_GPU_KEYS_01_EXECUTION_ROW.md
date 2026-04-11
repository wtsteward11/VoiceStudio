# GOV-VOICESTUDIO-GAP063-FIRST-RUN-WIZARD-MODELS-GPU-KEYS-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP063-FIRST-RUN-WIZARD-MODELS-GPU-KEYS-01 |
| **GAP** | GAP-063 (First-run wizard: models, GPU disclosure, API keys, telemetry, DI, exit-on-cancel) |
| **Status** | **Closed** (sealed with implementation + verification) |
| **Phase** | Bounded execution row — WinUI wizard + UI services seam |
| **Role** | UI Engineer + Core Platform (settings / backend seam) |

## §1 Objective (frozen)

Fix and complete the existing `FirstRunWizard` **Window** (no redesign): register `OnboardingWizardService`, add model readiness and API-key orientation, harden GPU CPU-fallback disclosure, route backend checks through `IDiagnosticsClient` + `IEnginesClient`, start backend via `BackendProcessManager.EnsureBackendRunningAsync`, show `TelemetryConsentDialog` once from **Loaded** (ADR-047), persist `WizardCurrentStep` for resume, and exit the app on cancel **only** on true first-run when the wizard was not completed.

## §2 Hard IN

- `OnboardingWizardService` registered singleton in `RegisterUIServices`; accessor on `AppServices`.
- Five steps: Welcome (+ telemetry consent gate), System check (+ GPU fallback panel), Model readiness (`IModelManagerClient.GetModelsAsync`), Backend health (`IDiagnosticsClient.CheckHealthAsync` + engines via `IEnginesClient`), API keys orientation + complete (skippable advisory) + `DontShowAgain`.
- `BackendProcessManager.EnsureBackendRunningAsync` for **Start Backend** (no raw `Process.Start` for uvicorn).
- `UnpackagedSettingsHelper`: `WizardCurrentStep`, `TelemetryConsentShown`; reset `WizardCurrentStep` to 1 on first-run completion.
- `FirstRunWizard(bool isFirstRun)`; `App.xaml.cs` calls `Application.Current.Exit()` only when `isFirstRun && !wizard.WasCompleted`.
- AutomationIds per registry; MSTests for seams + `ShouldShowWizard` settings contract.

## §3 Hard OUT

- Wizard shape remains `Window`; no change to `ShouldShowWizardAsync` contract semantics.
- No edits to default `OnboardingWizardService` step registry source list; no model download manager expansion; no settings IA redesign; no `WelcomeView` (`ContentDialog`) changes.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Show / skip wizard | `FirstRunWizard.ShouldShowWizardAsync` + `App.xaml.cs` gate |
| Resume step index | `UnpackagedSettingsHelper` `WizardCurrentStep` |
| Onboarding coordination (progress id) | `OnboardingWizardService.GetProgress()` (lightweight sync; no tooltip flow) |
| Model inventory | `IModelManagerClient.GetModelsAsync` |
| Backend health | `IDiagnosticsClient.CheckHealthAsync` |
| Engine list | `IEnginesClient.GetEnginesAsync` |
| Backend process | `BackendProcessManager.EnsureBackendRunningAsync` |
| Telemetry consent once | `TelemetryConsentShown` + `TelemetryConsentDialog` |

## §5 Acceptance criteria

- [x] `OnboardingWizardService` singleton + `GetOnboardingWizardService()` accessor.
- [x] Five steps, `TotalSteps = 5`, panels + indicator text updated; GPU fallback panel when no NVIDIA GPU.
- [x] Model readiness step calls `GetModelsAsync`; CTA when zero models (informational; completion allowed).
- [x] Backend health uses `IDiagnosticsClient` + `IEnginesClient` (no raw `/health` HttpClient in wizard).
- [x] Start Backend uses `BackendProcessManager.EnsureBackendRunningAsync`.
- [x] Telemetry consent from **Loaded**, `TelemetryConsentShown` gate, XamlRoot set.
- [x] `WizardCurrentStep` save on navigation; restore in ctor; cleared on `SaveFirstRunCompleteAsync`.
- [x] Exit-on-cancel: `isFirstRun` from `App.xaml.cs` only when `FirstRunComplete` is false at launch.
- [x] AutomationIds registered; `FirstRunWizardTests` (≥8) + build + App.Tests + `verify.ps1 -Quick` GREEN.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FirstRunWizard"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register (summary)

| Risk | Mitigation |
|------|------------|
| `CheckHealthAsync` / connection cache stale | Use `CheckHealthAsync` as primary; exceptions → disconnected UI state |
| `EnsureBackendRunningAsync` slow | Show "Starting..." then re-run health check |

## §8 Rollback order

1. `App.xaml.cs` exit gate + wizard ctor argument  
2. `FirstRunWizard` XAML + code-behind  
3. `AppServices` registration + accessor  
4. Tests + governance artifacts  
