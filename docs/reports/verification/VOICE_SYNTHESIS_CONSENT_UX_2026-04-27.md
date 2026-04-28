# Voice Synthesis Consent Recovery UX — Verification Report

**Date:** 2026-04-27  
**Scope:** Product lane (WinUI Voice Synthesis panel). **Not** GAP-008, **not** `MainWindow*ShellBridge`, **not** `ENGINE_PARITY_MATRIX`, **not** RHVoice. **Not** a runtime “FULL PASS” or in-app human attestation claim.

## Goal

When synthesis fails with `ConsentRequiredException`, the user sees a dedicated consent **InfoBar** with **Go to Profile** (navigates to Profiles via `NavigateToEvent` from `IEventAggregator`) and **Retry** (re-invokes synthesis). State clears on profile change, `ClearError`, or successful retry.

## Files Touched

| Area | Path |
|------|------|
| ViewModel | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` |
| View (XAML) | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` |
| Code-behind | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml.cs` |
| Tests | `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` |
| AutomationIds | `docs/developer/AUTOMATION_ID_REGISTRY.md` |

## Behavior (summary)

- `IsConsentRequired`, `ConsentRequiredProfileId`, `ConsentRequiredMessage` hold consent-specific state.
- Generic error `InfoBar` uses `ShowGenericSynthesisError` so it does not duplicate the consent surface when consent is active.
- `OpenProfileConsentCommand` publishes `NavigateToEvent(PanelIds.VoiceSynthesis, PanelIds.Profiles, profileId)`.
- `RetrySynthesisCommand` re-runs `SynthesizeAsync` when `IsConsentRequired && CanSynthesize`.
- `ClearConsentState()` integrated with `ClearError`, success path, profile change, and related partial property change notifications.

## Tests

- Consent recovery and non-consent (e.g. 403 / `AUTHORIZATION_FAILED`) cases covered in `VoiceSynthesisViewModelTests` (8+ methods in the consent-recovery region).

## Verification (2026-04-27)

Commands executed in order:

1. `dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis|FullyQualifiedName~Consent|FullyQualifiedName~Profile"`  
   - **Result:** Passed **249**, Failed **0** (42 skipped in same filter).
2. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`  
   - **Result:** **0** errors (pre-existing warnings in other files).
3. `python scripts/run_verification.py`  
   - **Result:** **Overall: PASS**  
   - **JSON:** `.buildlogs/verification/last_run.json`
4. `.\scripts\verify.ps1 -Quick`  
   - **Result:** **VERIFICATION PASSED**  
   - **Report:** `artifacts/verify/20260427_232009/verification_report.md`  
   - **Note:** `completion_guard` reported as **SKIP** in harness output (expected with `--skip-guard` in Quick); standalone `run_verification.py` included **completion_guard** **PASS**.

## Advisory (non-fail)

- `runtime_proof_staleness`, `slo_baseline_freshness`, `backend_smoke_freshness` — **STALE** advisories (warning-only; unchanged from repo policy).

## Control plane

- `.cursor/STATE.md` **ACTIVE WINDOW** and **LATEST PROOF INDEX** updated with this report and artifact paths.
