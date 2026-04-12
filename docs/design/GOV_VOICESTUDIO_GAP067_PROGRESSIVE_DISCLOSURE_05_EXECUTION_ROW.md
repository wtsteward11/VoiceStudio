# GOV-VOICESTUDIO-GAP067-PROGRESSIVE-DISCLOSURE-05 — Execution Row

**Status:** CLOSED (2026-04-12)  
**Lane:** GAP-067 Slice 5 — Progressive Disclosure Authority  
**Scope:** WinUI shell + Voice Synthesis + Timeline + Transcribe only (UI layer).

## Hard boundaries

**IN:** Primary / secondary / advanced disclosure rules; overflow/flyout, `Expander`, selection/state gating; AutomationIds; contract + seam tests; command reachability preserved.  
**OUT:** WCAG mega-sweep, theme/token overhaul, backend protocol changes, file activation / shell association (Slice 4 authority).

## Disclosure authority (canonical)

| Tier | Definition |
|------|------------|
| **Primary** | Actions required for the default happy-path on each surface (navigate, synthesize/transcribe core inputs, play/stop, status/job). |
| **Secondary** | Frequently used tuning that fits without crowding (profile/engine pick, basic sliders, add track). |
| **Advanced** | Power-user, diagnostic, or rarely toggled options; hidden behind explicit affordance (`Expander`, toolbar/status overflow). |

**Persistence:** Only high-value UI state: Voice Synthesis “advanced controls” expander (panel state).

## Phase 1 — Control density audit (frozen)

### Shell / chrome (MainWindow + CustomizableToolbar)

| Control / region | Tier | Notes |
|------------------|------|--------|
| Title bar, notification bell | Primary | Always visible |
| Menu + Command toolbar transport | Primary | Playback affordances |
| Project / engine / workspace (toolbar) | Secondary | Context |
| Global transport strip | Primary | Current media |
| Status: indicators + status text + job + progress | Primary | Operational |
| Status: CPU/GPU/RAM, sample rate, latency, collaborators | Advanced | Moved behind **System metrics** flyout |
| Toolbar: MOUT + latency bars (Performance section) | Advanced | Behind **Performance** flyout |

### Voice Synthesis

| Region | Tier |
|--------|------|
| Header, profile/engine/language/emotion row | Primary/secondary |
| Text input | Primary |
| Speed, pitch, enhance quality | Secondary (always visible) |
| Stability, clarity, temperature, mode checkboxes, streaming details | Advanced (`Expander`) |
| Long-form progress line | Primary when running (outside expander) |
| Synthesize / play / core actions row | Primary |

### Timeline

| Region | Tier |
|--------|------|
| Play, stop | Primary |
| Add track | Secondary |
| Record, loop | Advanced (overflow flyout) |
| Zoom | Advanced (overflow flyout) |
| Time display | Primary |
| Track header M/S/R disabled mixer | Advanced → **hidden** (not implemented) |

### Transcribe

| Region | Tier |
|--------|------|
| Audio ID, engine, language, Transcribe | Primary |
| Project ID, word timestamps, diarization, VAD | Advanced (`Expander`) |

## Phase 3 — Mechanism matrix

| Surface | Mechanism |
|---------|-----------|
| MainWindow status | `Button` + `Flyout` (`MainWindow_StatusBar_SystemMetricsButton`) |
| CustomizableToolbar | `Button` + `Flyout` (`CustomizableToolbar_PerformanceOverflowButton`) |
| Voice Synthesis | `Expander` (`VoiceSynthesisView_AdvancedControlsExpander`) |
| Timeline transport | `Button` + `Flyout` (`TimelineView_TransportMoreButton`) |
| Transcribe | `Expander` (`TranscribeView_AdvancedOptionsExpander`) |

## Acceptance

- [x] Rules frozen above; four surfaces aligned.
- [x] Advanced hidden by default with explicit affordance; commands unchanged.
- [x] `Gap067Slice5Tests` + seam toggles for VM disclosure flags.
- [x] Verification commands per closure report.

## Closure

See `docs/reports/verification/VOICESTUDIO_GAP067_PROGRESSIVE_DISCLOSURE_LANE_CLOSURE_2026-04-12.md`.
