# GOV-VOICESTUDIO-MULTITRACK-RECORDING-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-MULTITRACK-RECORDING-01`  
**Status:** **Closed** — Slice 1–**4** delivered (multitrack capture + recovery/operator-truth UX).  
**Tracker:** [GAP-042](PROFESSIONAL_GAP_TRACKER.md) — **Closed** — [lane closure report](../reports/verification/VOICESTUDIO_MULTITRACK_RECORDING_LANE_CLOSURE_2026-03-30.md)  

## Frozen objective

Deliver **project-scoped recording-session authority** with deterministic lifecycle semantics (**None → Prepared → Recording**), a single coordinator seam (`IRecordingSessionCoordinator` / `RecordingSessionCoordinator`), and **test-backed proof** for Slice 1. **Slice 2** adds real **timeline track target resolution** and **input device id assignment** (backend `RecordingDevice.Id`) with both surfaces sharing the same policy. **Slice 3** adds **executable multitrack capture fan-out** (one NAudio leg per armed assignment) with frozen validation and failure policy. **Slice 4** adds **multitrack recovery / operator-truth UX** (explicit restore/discard, per-leg outcomes, no silent loss of completed takes) and is required to **close the lane**.

## Authority map (frozen)

| Concern | Owner | Notes |
|--------|--------|--------|
| Recording session lifecycle (create / arm / disarm / start / stop / cancel) | `IRecordingSessionCoordinator` | In-memory; single app singleton |
| Track arm state (per session) | `IRecordingSessionCoordinator` | `ArmedTrackIds` / `TrackInputAssignments`; mutate only in **Prepared** |
| Track target resolution | `RecordingTrackTargetResolver` | Primary: `IContextManager.ActiveTimelinePrimaryTrackId` when on project list; else first track from `ITimelineTrackService.GetTracksAsync` |
| Track → input mapping | `IRecordingSessionCoordinator.TryArmTrack(trackId, inputSourceId)` | Slice 2–3: rejects empty input id, duplicate input across tracks; **Slice 3+** allows **multiple** distinct armed tracks |
| Command-path input (Ctrl+R) | `RecordingAuthorityResolver.ResolveForCommandPathAsync` | **GAP-035:** same resolvability policy as panel via `IRecordingInputCommandState` (last `SelectedInputDevice.Id` from Recording panel); **no** first-device fallback |
| Microphone / file capture | `MicrophoneRecordingService` + `RecordingCaptureFanoutService` | **Slice 3:** one **capture leg** per assignment; `RecordingInputDeviceResolver` maps backend `RecordingDevice.Id` / name to **NAudio** `WaveIn` device index; pre-start **all-or-nothing** validation |
| Project binding | `IRecordingSessionCoordinator.BindProject` | Precedence: binding **resets** session + assignments + armed state on project id change |
| UI / command entry points | `RecordingViewModel`, `PlaybackOperationsHandler` | Must use `RecordingSessionLifecycleGate` + real `trackId` + `inputSourceId` when coordinator is present |
| Multitrack recovery payload + apply | `IMultitrackRecoveryStateService`, `IMultitrackRecoveryApplyService` | **Slice 4:** persist/read JSON under `SessionState.CustomState`; restore uploads + `SaveAudioToProject`; discard deletes listed WAVs |
| Recovery prompt UX | `MainWindowSessionLifecycle` | Extends crash **Restore / Discard** dialog; multitrack summary; no restore until startup ready |

## Binary acceptance (lane)

| Slice | Acceptance |
|-------|------------|
| **1 — Lifecycle authority** | Coordinator registered in DI; `RecordingViewModel` + `PlaybackOperationsHandler` use gate for start/stop/cancel; cannot start with zero armed tracks; arm/disarm only in Prepared; stop/cancel idempotent-safe; project switch resets session; unit tests + verification matrix green. |
| **2 — Track / input assignment** | No placeholder track on live start path; coordinator stores `trackId` + backend input id; single-track arm policy enforced; duplicate input across tracks rejected; Recording panel ComboBox binds device **id** (model) + **name** (display); Ctrl+R uses resolver + first backend device id when coordinator present; targeted tests + full verification matrix green; lane remains **open**. |
| **3 — Multitrack capture fan-out** | **Capture leg** = runtime unit `(trackId, inputSourceId, status, output path)`; **pre-start:** all legs resolvable or **none** start; **mid-record failure:** fail-fast stop of session, **preserve** completed leg WAVs, failed legs marked in fan-out outcome; **Ctrl+R:** always **single-track** (session reset before shortcut so no inherited multitrack arms); Recording panel uses `RecordingCaptureFanoutService` for N-leg capture; per-leg upload + project save with deterministic filenames; tests + matrix green; Slice 3 prerequisite for Slice 4 (lane closed with Slice 4 — see closure report). |
| **4 — Recovery / UX** | Typed **multitrack recovery payload** in `CrashRecoveryService.SessionState.CustomState` (`recording.multitrackRecovery.v1`); **no silent restore**; **Restore completed takes** / **Discard pending recovery**; failed legs listed; **clean stop** clears pending payload; **discard** deletes referenced preserved temp WAVs and clears snapshot; recovery prompt **gated on `IStartupStateService.IsReady`** (defer until backend ready); Recording panel shows **per-leg / session outcome**; tests + verification matrix + closure report green. |

## Slice 1 progress (2026-03-30)

- [x] Execution row + registry + tracker + STATE opened for GAP-042  
- [x] `IRecordingSessionCoordinator` contract frozen (Core) + `RecordingSessionCoordinator` (App)  
- [x] `RecordingSessionLifecycleGate` shared gate + `RecordingSessionSlice1Defaults.PrimaryInputTrackId`  
- [x] DI: `AppServices` + `AppServicesAdapter` + `CommandHandlerBootstrapper`  
- [x] Integration: `RecordingViewModel`, `RecordingView`, `PlaybackOperationsHandler`  
- [x] Tests: `RecordingSessionCoordinatorTests` + seam guard (`PlaybackOperationsHandler` no-project)  
- [x] Verification: build, App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` (**completion_guard**)  

## Slice 2 — Frozen contract (track / input assignment)

### Objective (binary)

Replace placeholder **track identity** with **project timeline track authority** and add **explicit input source ids** (`RecordingDevice.Id` from `/api/recording/devices`) on the coordinator arm operation, shared by Recording panel and Ctrl+R.

### Policy table

| Rule | Slice 2 decision |
|------|------------------|
| Valid recordable track ids | Members of `ITimelineTrackService.GetTracksAsync(projectId)`; resolved by `RecordingTrackTargetResolver` |
| Input identity | Canonical: device **Id**; UI label: **Name** |
| One input → multiple tracks | **Reject** (defer to Slice 3) |
| Multiple armed tracks | **Reject** in Slice 2 policy — **Slice 3+** allows multiple distinct armed tracks (see Slice 3) |
| Duplicate input mapping | **Reject** if two tracks would share the same input id |
| Project rebind mid-session | `BindProject` different id → full reset (Phase `None`, assignments cleared) |
| Unknown / empty track or input | **Reject** with explicit error message |

### Hard IN (Slice 2)

- `TryArmTrack(trackId, inputSourceId)`; `TrackInputAssignments` / `RecordingSessionStatus` includes map; lifecycle gate takes `inputSourceId`.
- `RecordingTrackTargetResolver`, `RecordingAuthorityResolver` (command path), `RecordingView.xaml` device binding by model.

### Hard OUT (Slice 2)

- Multitrack capture fan-out (Slice 3); NAudio device index tied to backend id (GAP-035); PanelHost; lane closure claim.

### Rollback (Slice 2)

- Revert coordinator arm signature + gate extra parameter; restore placeholder track only in legacy tests; revert panel ComboBox to name-only (document reason in this row).

## Slice 2 progress

- [x] Execution row Slice 2 section + policy freeze  
- [x] Core + coordinator assignment state + gate signature  
- [x] Resolvers + AppServices `TryGet` seams  
- [x] Recording panel + Ctrl+R wiring  
- [x] Tests: coordinator + resolver + playback authority fail  
- [x] Verification matrix + STATE proof row  

## Slice 3 — Frozen policy (final capture fan-out)

### Capture leg (definition)

- Runtime object bound to exactly one `trackId` and one `inputSourceId` (backend device id), with NAudio capture, output path, and completion / failure status.

### Pre-start validation

- **All-or-nothing:** if any armed assignment cannot be resolved to a WaveIn device index, **no** leg starts and coordinator MUST NOT remain stuck in `Recording` (fan-out validates before `TryStartRecording`, or unwinds on mic start failure).

### Mid-record failure

- **Fail-fast:** any leg error stops **all** active legs deterministically, preserves **completed** WAV files on disk for successful legs, and exposes failed leg ids/messages in the fan-out result for UI / tests.

### Ctrl+R (playback.record)

- **Single-track convenience only:** before preparing the shortcut session, **cancel** any in-memory coordinator session so Ctrl+R never inherits a multitrack-prepared arm set from the panel. One capture pipeline for the resolved authority track + input.

### Hard IN (Slice 3)

- Multi-arm coordinator; `RecordingCaptureFanoutService`; `RecordingInputDeviceResolver`; per-leg `MicrophoneRecordingService` instance (or equivalent leg handle); panel start/stop/cancel through fan-out; deterministic tests for arm policy and fan-out orchestration (stub legs where NAudio is unavailable).

### Hard OUT (Slice 3)

- Slice 4 recovery UX; PanelHost / metering lane changes; unrelated polish.

## Slice 3 progress

- [x] Execution row Slice 3 policy freeze  
- [x] Coordinator multi-track arm + fan-out + device resolution  
- [x] Recording panel + Ctrl+R policy (single-track shortcut)  
- [x] Per-leg persistence path (upload + `SaveAudioToProject` per leg)  
- [x] Tests + verification matrix + STATE proof row (lane **open** for Slice 4)  

## Slice 4 — Frozen policy (recovery / operator truth)

### Recoverable multitrack session

A **recoverable** state exists when **all** are true:

- `SessionState.CustomState` contains key `recording.multitrackRecovery.v1` with valid JSON payload (`MultitrackRecoveryPayload`).
- Payload `EndedCleanly == false`.
- Payload includes at least one leg with **Completed** status and a **non-empty** `PreservedOutputPath`, **or** at least one **Failed** leg with an explicit message (operator must see partial/disrupted outcome).

### Payload (required fields)

- `schemaVersion`, `projectId`, `sessionId` (coordinator `ActiveSessionId`), `createdAtUtc`, `endedCleanly`
- Per leg: `trackId`, `inputSourceId`, `status` (completed / failed / missing), `preservedOutputPath?`, `failureMessage?`

### When recovery is offered

1. **App launch / cold start:** existing `CrashRecoveryService` path — `PendingRecoveryDetermined` → **Restore / Discard** dialog in `MainWindowSessionLifecycle` after `Loaded` (XamlRoot). If multitrack payload present, dialog shows **counts + failed-leg summary**.
2. **While running:** entering **Recording** panel **does not** prompt by itself; operator sees **last session outcome** Infobar in panel if applicable. Pending crash snapshot still triggers startup dialog on **next** launch.
3. **Startup gate:** if `!IStartupStateService.IsReady`, **do not consume** recovery choice — defer dialog until **Ready** (re-prompt via startup `StateChanged`), so `OpenRecentProjectAsync` is not a no-op.

### Clean stop vs fail-fast

| Outcome | Pending multitrack payload |
|--------|----------------------------|
| User **Stop** and all legs complete + upload/save path owner clears | **Clear** payload; no later recovery prompt for that session |
| **Fail-fast** (leg error) or partial completion | **Write** payload with per-leg truth; completed WAV paths preserved |
| User **Cancel** (discard intent) | **No** multitrack recovery payload (cancel deletes temps per fan-out) |
| **Discard** in recovery dialog | Remove payload key; **delete** every `preservedOutputPath` listed for completed legs; then discard session snapshot per existing `CrashRecoveryService` rules |
| **Restore** | Open project via `OpenRecentProjectAsync`; **block** if active `projectId` ≠ payload `projectId`; then upload + `SaveAudioToProject` for each completed leg with existing file |

### Hard IN (Slice 4)

- `RecordingCaptureFanoutService` fault drain produces deterministic `RecordingCaptureStopResult` for VM recovery wiring  
- `IMultitrackRecoveryStateService` + `IMultitrackRecoveryApplyService`  
- Extended recovery dialog + panel outcome Infobar  

### Hard OUT (Slice 4)

- PanelHost; metering; timeline editor redesign; generic recording refactors unrelated to recovery  

## Slice 4 progress

- [x] Execution row Slice 4 policy freeze + binary acceptance row  
- [x] Recovery models + state/apply services + DI  
- [x] Fan-out fault outcomes → VM recovery write; clean stop clears payload  
- [x] `MainWindowSessionLifecycle` multitrack dialog + startup gate + restore/discard file policy  
- [x] Recording panel outcome UX  
- [x] Tests + verification matrix + closure report + tracker/registry/STATE  

## Hard IN (Slice 1)

- Coordinator seam + gate helpers; placeholder primary track id for single-mic surfaces.  
- Both **Recording panel** and **Ctrl+R** (`PlaybackOperationsHandler`) go through the same lifecycle checks.  

## Hard OUT

- PanelHost GAP-007; multitrack waveform/editor redesign; plugin hosting; mastering / true-peak / LUFS expansion; transport redesign; GAP-036 regression work; full multitrack routing (Slice 2+).  

## Lifecycle contract (binary)

1. **Transitions:** `None → Prepared → Recording` only (cancel → `None`).  
2. **Start:** Blocked if no armed tracks; blocked if not `Prepared`.  
3. **Double start:** Idempotent **no-op** while already `Recording` (`TryStartRecording` returns true).  
4. **Arm / disarm:** Only in `Prepared`.  
5. **Stop (from Recording):** Deterministic return to `Prepared` with armed set cleared.  
6. **Cancel:** Full reset to `None`.  
7. **Project change:** `BindProject` invalidates prior session state.  

## Rollback

1. Remove gate calls from `RecordingViewModel` / `PlaybackOperationsHandler`.  
2. Remove coordinator DI registration and adapter mapping.  
3. Keep this execution row open; record rollback reason under Slice 1 progress.  
4. Do not reopen closed lanes (e.g. GAP-036) unless a regression is proven.  

## Dependencies

- **GAP-035** (device-change robustness): follow-on; **not** a blocker for Slice 1 lifecycle authority.  
