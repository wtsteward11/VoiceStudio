# GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01 — Execution row

**Status:** Closed (2026-03-28)  
**Tracker:** GAP-009 — **Closed** (see [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md))  
**Objective:** Unify timeline transport and global transport into one truthful, authoritative playback / record / loop state model so no visible transport control is decorative or contradictory.

## Binary acceptance (frozen)

| Slice | Acceptance |
|-------|------------|
| 1 — Command authority on timeline bar | Every visible control on the timeline transport strip binds to real commands or is explicitly disabled with honest tooltip. Time display reflects the chosen playback time source. Record and Loop are honest (wired or disabled), not inert chrome. |
| 2 — Global vs timeline unification | Timeline play/stop/pause and global bar + keyboard shortcuts mutate the same underlying transport path for the same `CurrentPlayableSource`. No contradictory visible state between surfaces. |
| 3 — Playhead and seek truth | Seek, preview, and playback position use one canonical time source; `SetCurrentPlayable` / context transport fields do not drift from timeline UI. |
| 4 — Proof + closure | Targeted MSTest; `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`; `dotnet test` App.Tests; `python -m pytest tests/ci/ -q --randomly-seed=12345`; `.\scripts\verify.ps1 -Quick`; `python scripts/run_verification.py` (**completion_guard** PASS). Closure report under `docs/reports/verification/`. |

## Hard OUT (not this lane — unchanged by closure)

- This lane did **not** implement persistence, SQLite, unified project save (GAP-016–018, GAP-021) — **now unblocked:** `GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01` may open as the next execution lane.
- Waveform editing, metering, transcript ↔ clip linkage, effects-in-export, text-based audio editing.
- MainWindow mega-refactor beyond transport shortcut / orchestrator touch points required here.
- PanelHost GAP-007, plugin/DAW scope.
- New roadmap or governance doc waves beyond this row + mandatory registry/STATE sync at close.

## Next lane (unblocked after this close)

1. **`GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01`** — GAP-016, GAP-017, GAP-021 (authoritative project model, timeline + mixer/layout metadata persistence contract, deterministic reopen). **Out of scope for first persistence cut:** migration empire, export redesign, waveform editing, collaboration, transcript editing, telemetry expansion — freeze in that lane’s execution row when created.

---

## §2 Frozen transport surface inventory (code-truth at row open)

_Order: timeline strip → global → shortcuts → seek/playhead._

| Affordance | Visible surface | Current binding / behavior | Authority owner | Mismatch? |
|------------|-----------------|---------------------------|-----------------|-----------|
| Timeline play | `TimelineView.xaml` Play button | `PlayAudioCommand` → `PlayAudioAsync` / `IAudioPlayerService` | `TimelineViewModel` + `IAudioPlayerService` | Global bar uses `IGlobalTransportOrchestrator` → `ITimelineTransportController` when source is Timeline — **same VM commands** if controller wraps `PlayAudioCommand` (yes); still verify pause/resume parity. |
| Timeline stop | Timeline Stop | `StopAudioCommand` | Same | Same as above for orchestrator `Stop()` path. |
| Timeline record | Timeline Record button | **None** (decorative) | — | **Yes** — must bind to honest action (e.g. navigate to Recording) or disable. |
| Timeline loop | Timeline Loop `ToggleButton` | **None** (decorative) | `IAudioPlayerService.IsLooping` exists on service | **Yes** — must two-way bind or disable. |
| Timeline time | `TimePositionDisplay` | Hardcoded `00:00.000` | `TimelineViewModel.CurrentPlaybackPosition` updated from `PositionChanged` | **Yes** — display does not bind to VM. |
| Global play/pause | `GlobalTransportControl` | `PlayRequested` → MainWindow → `TogglePlaybackAsync` | `GlobalTransportOrchestrator` | OK as router; must stay consistent with timeline when source is Timeline. |
| Global stop | Global Stop | `StopRequested` → `StopPlayback` | `GlobalTransportOrchestrator` | OK. |
| Global pause/resume | Via same toggle as play | Orchestrator + `IAudioPlayerService` for library path | Same | Confirm timeline path uses controller pause/resume same as timeline buttons. |
| Keyboard Space | `TransportShortcutCoordinator` | `_orchestrator.TogglePlaybackAsync()` | `GlobalTransportOrchestrator` | Must match global + timeline button outcomes. |
| Keyboard S | Shortcuts | `_orchestrator.StopPlayback()` | Same | Same. |
| Keyboard Ctrl+R | Shortcuts | `toggleRecord` callback from MainWindow | MainWindow-local until unified | **Dual path risk** — align with timeline Record policy in Slice 1–2. |
| Playhead / seek | `TimelineScrubCanvas` pointer handlers | Code-behind → VM seek/scrub | `TimelineViewModel` | Slice 3: verify against same time source as transport display and `IAudioPlayerService` position. |

### Per-track M / S / R / volume (template)

| Control | Binding | Authority | Mismatch? |
|---------|---------|-----------|-----------|
| Mute / Solo / Record arm / volume | **None** (static slider `0.8`) | N/A | **Yes** — honest disable + tooltip until mixer/timeline arm exists, or wire to real track state. |

---

## §3 Canonical owner decision (frozen for this execution)

**Chosen pattern: Option C (shared state + single command router), implemented incrementally.**

- **Playback engine state** (position, looping, is playing/paused for backend-audio paths): **`IAudioPlayerService`** remains the low-level truth.
- **Command routing** from shell (global bar, keyboard): **`IGlobalTransportOrchestrator`** remains the single entry for Space/S and global buttons; timeline with `TransportSource.Timeline` already resolves via `ITimelineTransportController`.
- **Required convergence (Slices 1–2):** Timeline transport buttons must not bypass the orchestrator in ways that leave global UI stale, and must not duplicate divergent play/stop logic. Prefer injecting or delegating to the same code paths the orchestrator uses, or calling orchestrator from `TimelineViewModel` for play/stop when appropriate — **exact wiring is an implementation detail** as long as acceptance tests prove one behavioral authority.

**Rejected:** Indefinite dual authority (timeline-only and global-only play logic with no contract).

---

## §4 Record / loop policy (frozen at row open)

| Control | Meaning today | Row requirement |
|---------|---------------|-----------------|
| **Loop** | `IAudioPlayerService.IsLooping` is implemented in `AudioPlayerService` — playback loop for backend audio. | Timeline Loop toggle **must** reflect and set this property (or be removed/disabled with explanation). |
| **Record** | No timeline-local engine record arm in VM; in-app capture lives under **Recording** panel (`PanelIds.Recording`). | Timeline Record **must** either navigate to Recording (`NavigateToEvent`) or be disabled with tooltip — **no silent no-op.** |
| **Ctrl+R** | Callback from MainWindow; may differ from timeline button until unified. | After Slice 2, **same policy** as timeline Record (navigate or disable). |

---

## §5 Implementation notes (Slice 1 — immediate)

1. **`TimelineView.xaml`:** Bind time to `TransportTimeDisplay`; Record → `OpenRecordingFromTimelineCommand`; loop → **`ToggleSwitch` `IsOn`** TwoWay to `IsTimelineLoopEnabled` (WinUI `ToggleButton.IsChecked` is `bool?` — x:Bind to VM `bool` fails MarkupCompile). Per-track M/S/R/slider: **`IsEnabled="False"` on each control** — `StackPanel IsEnabled="False"` in this `DataTemplate` triggered XamlCompiler exit 1 (WinUI 1.8); tooltips on children.
2. **`TimelineViewModel.cs`:** `_eventAggregator` / `_contextManager` **before** commands; `TransportTimeDisplay`; `IsTimelineLoopEnabled` + ctor sync from `_audioPlayer.IsLooping`; `OpenRecordingFromTimeline` publishes `NavigateToEvent(PanelId, PanelIds.Recording)`.

**Slice 1 proof:** [VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE1_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE1_PROOF_2026-03-28.md)

---

## §6 Slice 2 — Global / timeline / keyboard convergence (frozen)

**Objective:** One behavioral command path for `TransportSource.Timeline` across timeline strip, `GlobalTransportControl`, Space/S shortcuts, and `PlaybackOperationsHandler` pause/toggle when orchestrator is injected.

**Chosen wiring (implementation truth):**

- **Router:** `IGlobalTransportOrchestrator` remains the shell entry for global play toggle, stop, and explicit pause used by command palette.
- **Timeline execution:** `ITimelineTransportController` on `TimelineViewModel` — `PlayAsync` resumes when `IAudioPlayerService.IsPaused` (parity with library path in orchestrator); `Pause` / `Stop` unchanged.
- **Fallback:** If `CurrentPlayableSource == Timeline` but `GetTimelineController()` is null, orchestrator drives `IAudioPlayerService` pause/resume/stop so Space/global do not no-op while audio is active.
- **Ctrl+R:** Same policy as timeline Record button — `NavigateToEvent(Timeline → Recording)` via event aggregator (no MainWindow-only record toggle on this shortcut).

**Binary acceptance (Slice 2):**

1. With `CurrentPlayableSource == Timeline`, global toggle/stop and timeline strip use the same controller semantics (including pause → resume without restarting preview).
2. Space and S invoke the same orchestrator methods as global transport buttons.
3. Ctrl+R publishes the same navigation intent as `OpenRecordingFromTimelineCommand` (Recording panel target).
4. No contradictory transport state between surfaces for the timeline-owned path (play → pause → resume → stop).
5. Slice 1 timeline honesty (record/loop/time, disabled track chrome) remains intact.

**Out of scope (Slice 2):** `playback.record` command palette behavior (microphone toggle) — shortcut path only aligned with timeline Record; lane Slice 3+ unchanged.

**Proof:** [VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE2_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE2_PROOF_2026-03-28.md).

---

## §7 Slice 3 — Playhead and seek truth (frozen)

**Objective:** One canonical time model for timeline-owned playback: `IAudioPlayerService.Position` flows through `CurrentPlaybackPosition` to `TransportTimeDisplay`, `PlayheadPosition`, and `IsPlayheadVisible`; stop and preview semantics are deterministic; context transport fields do not drift from timeline UI for the frozen policies below.

**Canonical time (implementation truth):**

- **`CurrentPlaybackPosition`** is the single VM time source for transport display and playhead (`PlayheadPosition = CurrentPlaybackPosition * PIXELS_PER_SECOND * TimelineZoom`).
- **`IAudioPlayerService.PositionChanged`** updates `CurrentPlaybackPosition` during playback; seek paths write **`Seek()` + `CurrentPlaybackPosition`** together.

**Binary acceptance (Slice 3):**

1. `CurrentPlaybackPosition` is the single time source for transport display and playhead.
2. Seek writes through `IAudioPlayerService.Seek()` and updates `CurrentPlaybackPosition` atomically (same method body).
3. **Stop** resets `CurrentPlaybackPosition` to **0.0** deterministically (frozen policy: not DAW-style stop-retains-head; documented in proof).
4. Preview scrubbing sets/clears `IsPreviewing` correctly; code-behind scrub release after `StopPreview()` clears `IsPreviewing` on the VM so stale state cannot persist if the preview completion callback never runs.
5. **`SetCurrentPlayable` / context transport:** Identity is set when timeline play starts; it is **not** required on seek/pause (identity unchanged). **`CurrentPlayableSource` / timeline ownership** follows **last-writer-wins** and is **not** cleared on stop (consistent with Library/Synthesis); frozen policy, not a bug.

**Out of scope (Slice 3):** Persistence (GAP-016+), PanelHost GAP-007, metering, transcript linkage, export, waveform editing.

**Proof:** [VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE3_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE3_PROOF_2026-03-28.md).

---

## §8 Slice 4 — Lane closure (frozen)

**Objective:** Binary closure matrix, governance sync (STATE, registry, gap tracker), and verification gates on the closure commit. **No additional transport feature work.**

**Deliverable:** [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md)

**Binary acceptance (Slice 4):**

1. Closure report maps Slices 1–3 + lane-wide verification to proof artifacts and verdicts.
2. Non-goals and honest limits are explicit (persistence, DAW-class claims, command-palette record asymmetry, MSB3027 / suite signal honesty).
3. Execution row status **Closed**; GAP-009 **Closed**; registry + STATE updated; next queued work is Persistence Foundation only.

**Lane closure proof:** same document as §8 deliverable.

---

## Changelog

| Date | Note |
|------|------|
| 2026-03-28 | Row opened; inventory + authority + record/loop policy frozen; registry + gap tracker + STATE queued lane aligned. |
| 2026-03-28 | **Slice 1 complete** — VM + XAML + `TimelineViewModelTests`; proof `VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE1_PROOF_2026-03-28.md`; verify `artifacts/verify/20260328_044821/`; **lane remains Open** until Slices 2–4. |
| 2026-03-28 | **Slice 2 frozen** — §6 convergence contract + router/controller/fallback/Ctrl+R policy; proof path reserved. |
| 2026-03-28 | **Slice 2 complete** — orchestrator timeline fallbacks + `PausePlayback`; timeline resume parity; Ctrl+R → `NavigateToEvent` (Recording); `PlaybackOperationsHandler` pause/toggle via orchestrator; `GlobalTransportOrchestratorTests` + `TransportShortcutCoordinatorTests` + timeline resume tests; proof `VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE2_PROOF_2026-03-28.md`; verify `artifacts/verify/20260328_052954/`; **lane remains Open** until Slices 3–4. |
| 2026-03-28 | **Slice 3 frozen** — §7 time truth + stop-to-zero + preview VM cleanup + context last-writer-wins policy; proof path `VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE3_PROOF_2026-03-28.md`; **lane remains Open** until Slice 4 closure. |
| 2026-03-28 | **Slice 3 complete** — `StopAudio` position reset; scrub `IsPreviewing` cleanup; `TimelineViewModelTests` Slice 3 cases; proof [VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE3_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_SLICE3_PROOF_2026-03-28.md); verify `artifacts/verify/20260328_060039/`; **lane remains Open** until Slice 4. |
| 2026-03-28 | **Lane closed (Slice 4)** — §8 closure matrix + [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md); GAP-009 Closed; verify path on closure commit recorded in `.cursor/STATE.md`; **Persistence Foundation** unblocked. |
