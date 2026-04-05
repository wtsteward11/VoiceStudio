# GOV-VOICESTUDIO-DEVICE-CHANGE-ROBUSTNESS-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-DEVICE-CHANGE-ROBUSTNESS-01`  
**Status:** **Closed** 2026-03-30 — GAP-035 device churn + operator truth (prepared / active / recovery / Ctrl+R). Closure: [VOICESTUDIO_DEVICE_CHANGE_ROBUSTNESS_LANE_CLOSURE_2026-03-30.md](../reports/verification/VOICESTUDIO_DEVICE_CHANGE_ROBUSTNESS_LANE_CLOSURE_2026-03-30.md).  
**Tracker:** [GAP-035](PROFESSIONAL_GAP_TRACKER.md)  

## Frozen objective

Make recording behavior **deterministic and truthful** when capture devices appear, disappear, reorder, or when the system default changes—without reopening **GAP-042** (multitrack lane closed) and without an ASIO/WASAPI engine redesign.

## Authority map (frozen)

| Concern | Owner | Notes |
|--------|--------|-------|
| Device listing + churn signals | `IRecordingDeviceAvailabilityService` | Backend `/api/recording/devices` snapshot + NAudio WaveIn fingerprint on refresh; **active capture** also polls `RecordingCaptureTopology` (~400ms) for WaveIn churn |
| Backend id → WaveIn index | `RecordingInputDeviceResolver` | Must fail closed on unknown id, missing snapshot entry, **ambiguous** name matches, and invalid `default` resolution |
| Command-path input selection | `IRecordingInputCommandState` | **Recording panel** selection is canonical for Ctrl+R (no “first device” shortcut) |
| Prepared / start gating | `RecordingViewModel`, `RecordingSessionLifecycleGate` | Validate resolvability **before** durable phase transition where possible |
| Active capture | `RecordingCaptureFanoutService` | On device churn while recording: **fail-fast** session; preserve completed leg WAVs; explicit fault |
| Recovery UX | `MainWindowSessionLifecycle`, `IMultitrackRecoveryStateService` | Restore completed takes allowed without hardware; **no** implied “ready to resume capture” without devices |
| Ctrl+R | `PlaybackOperationsHandler`, `RecordingAuthorityResolver` | Same resolvability policy as panel path |

## Policy (frozen)

### Prepared

- If any armed `inputSourceId` is **not** resolvable to a WaveIn device: **block start** with an explicit reason.
- **No** silent reassignment to another microphone when the selected id disappears from the machine.

### Active recording

- Device loss / churn during capture: **fail-fast** session policy; completed outputs **preserved**; failed legs **explicitly** marked.

### Recovery

- Import / restore of **completed** takes remains allowed when capture devices are unavailable.
- Messaging must not imply the user can **continue capture** without checking the Recording panel and device availability.

### Ctrl+R

- Must run the **same** resolvability checks as the panel path.
- **No** random fallback device when resolution fails.

## Hard IN

- `IRecordingDeviceAvailabilityService` + `RecordingDeviceAvailabilityService` (DI singleton).
- `IRecordingInputCommandState` + panel synchronisation of `SelectedInputDevice.Id`.
- Resolver ambiguity + `default` handling; fan-out **mid-session** churn handling via availability events.
- Targeted tests (resolver, fan-out, command path, recovery copy).

## Hard OUT

- ASIO/WASAPI capture stack rewrite; PanelHost / metering / timeline redesign; plugin work; reopening **GAP-042** unless regression-proven.

## Binary acceptance

- [x] Resolver returns deterministic errors: missing id, stale id, `default` path, ambiguous name mapping (tests + policy).
- [x] Panel: prepared arm validates resolvability; no silent ComboBox remap when prior id disappears; churn refreshes Prepared validation.
- [x] Fan-out: leg error / fail-fast + preserved outputs (existing `RecordingCaptureFanoutServiceTests`); active topology poll revalidates legs.
- [x] Ctrl+R: uses `IRecordingInputCommandState` + same resolver/availability as panel; no first-device fallback.
- [x] Recovery dialog / follow-up toast: import-without-mic vs new capture (`MultitrackRecoveryOperatorCopy`).
- [x] Verification matrix executed for closure (closure report).

## Honest limits

- Backend device **indices** vs WaveIn ordering can still diverge on some drivers; **name-first** resolution with ambiguity failure is the supported policy. Numeric `RecordingDevice.Id` is used only when it is a **unique** disambiguator within WaveIn capability enumeration.

## Rollback

- Remove availability service registration and fan-out subscription; restore prior resolver and Ctrl+R first-device behavior; document failed acceptance item in this row.
