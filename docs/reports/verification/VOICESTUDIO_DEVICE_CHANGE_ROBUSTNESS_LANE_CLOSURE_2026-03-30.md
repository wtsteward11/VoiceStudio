# GAP-035 / GOV-VOICESTUDIO-DEVICE-CHANGE-ROBUSTNESS-01 — Lane closure

**Date:** 2026-03-30  
**Tracker:** [GAP-035](../../design/PROFESSIONAL_GAP_TRACKER.md) → **Closed**  
**Execution row:** [GOV_VOICESTUDIO_DEVICE_CHANGE_ROBUSTNESS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_DEVICE_CHANGE_ROBUSTNESS_01_EXECUTION_ROW.md)  

## Summary

Delivered bounded device-churn robustness: central availability snapshot + WaveIn fingerprint, deterministic `RecordingInputDeviceResolver` (including `default` + ambiguity), Recording panel → `IRecordingInputCommandState` for Ctrl+R parity, multitrack fan-out **topology polling** during active capture with revalidation, recovery UX copy + post-restore guidance, and targeted unit tests.

**Non-goal honored:** GAP-042 multitrack lane not reopened; no ASIO/WASAPI stack rewrite.

## Proof artifacts (commands)

Authoritative run (2026-03-30):

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — PASS  
2. `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` — PASS (**2886** passed, 274 skipped)  
3. `python -m pytest tests/ci/ -q --randomly-seed=12345` — PASS (**216** passed)  
4. `.\scripts\verify.ps1 -Quick` — PASS → `artifacts/verify/20260330_195159/`  
5. `python scripts/run_verification.py` — **completion_guard** PASS; `.buildlogs/verification/last_run.json`

## Code touchpoints

| Area | Files |
|------|--------|
| Availability / selection | `IRecordingDeviceAvailabilityService`, `RecordingDeviceAvailabilityService`, `RecordingCaptureTopology`, `IRecordingInputCommandState`, `RecordingInputCommandState` |
| Resolver | `RecordingInputDeviceResolver` |
| Fan-out | `RecordingCaptureFanoutService` (topology timer, `ActiveLeg.InputSourceId`) |
| DI | `AppServices` |
| Panel / VM | `RecordingViewModel`, `RecordingView.xaml.cs` |
| Ctrl+R | `RecordingAuthorityResolver`, `PlaybackOperationsHandler` |
| Recovery copy | `MultitrackRecoveryOperatorCopy`, `MainWindowSessionLifecycle`, `RecordingRecoveryModels` |
| Tests | `RecordingInputDeviceResolverTests`, `RecordingDeviceAvailabilityServiceTests`, `MultitrackRecoveryOperatorCopyTests`, `RecordingInputCommandStateTests` |

## Honest limits

WaveIn **name** collision still yields a closed failure (by design). Backend ordering vs WaveIn index can diverge on some drivers; numeric `RecordingDevice.Id` is accepted only when it matches the named WaveIn capability at that index.
