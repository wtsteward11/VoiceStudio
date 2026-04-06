# Premium Software Coherence Audit

**Status:** Living  
**Created:** 2026-03-17  
**Source:** Premium Reliability and Coherence Pass (Task 12)  
**Purpose:** Formal audit artifact for future prioritization; repo-backed findings only.

---

## Executive Summary

VoiceStudio is feature-heavy with 47+ panels, 164+ API endpoints, and 44 engine manifests. This audit assesses coherence across startup, shell, transport, panel lifecycle, event wiring, backend seam, user workflows, and UX. Gaps are ranked by severity (S0–S2) for prioritization.

**Key finding:** Startup, transport, and shell decomposition have been hardened (2026-03). Remaining gaps center on workflow continuity, discoverability, and feature-island integration.

---

## 1. Startup / Readiness

| Aspect | Status | Evidence |
|--------|--------|----------|
| Backend auto-start | ✅ Implemented | `BackendProcessManager`, `EnsureBackendWithTrackingAsync`; explicit phases in `OnLaunched` |
| Startup states | ✅ Implemented | `Starting`, `BackendStarting`, `BackendReady`, `BackendFailed`, `Degraded` in `StartupStateService` |
| Staged readiness telemetry | ✅ Implemented | `BackendProcessManager` logs: process started, stdout/stderr, TCP reachability, `/health` success |
| Startup diagnostics | ✅ Implemented | `%LOCALAPPDATA%\VoiceStudio\logs\startup-{timestamp}.log`; categorized failures |
| Installed layout | ✅ Documented | `docs/design/INSTALLED_LAYOUT_REFERENCE.md`; `FindAppRoot`, `HasBackendMarker` validated |
| Icon-launch smoke | ✅ Implemented | verify.ps1 Stage 8.6; app run with `--icon-launch-smoke` |
| Failure-path smoke | ✅ Implemented | verify.ps1 Stage 8.7; `scripts/icon-launch-failure-smoke.ps1` (port-occupied path) |
| Runtime-missing smoke | ✅ Implemented | verify.ps1 Stage 8.8; `runtime-missing-failure-smoke.ps1` |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Full verify.ps1 not run in CI; manual run recommended | `.cursor/STATE.md`; verify.ps1 ~15+ min |
| S2 | First-launch cold backend can take ~40s+; UI budget must exceed worst case | `BackendProcessManager` `StartupReadinessTimeoutSeconds` + [UI startup boundary proof](../reports/verification/VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md) |

---

## 2. Shell / Navigation

| Aspect | Status | Evidence |
|--------|--------|----------|
| Navigation shell extraction | ✅ Implemented | `IShellNavigationCoordinator`, `ShellNavigationCoordinator`; `OpenPanelByIdAsync`, `ExecuteNavCommandAsync`, `ResolvePanelIdAlias`, `GetPanelRegion`, `GetPanelTitle` |
| MainWindow delegation | ✅ Implemented | MainWindow delegates to coordinator; [MAINWINDOW_DECOMPOSITION_PLAN](MAINWINDOW_DECOMPOSITION_PLAN.md) |
| Transport shortcuts | ✅ Implemented | `TransportShortcutCoordinator` — Space, S, Ctrl+R |
| Import workflow | ✅ Implemented | `IImportWorkflowService`, `ImportWorkflowService` |
| Status bar | ✅ Implemented | `StatusBarCoordinator` — attach/subscribe/unsubscribe |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S1 | Search overlay / toolbar glue logic still in MainWindow | `MainWindow.xaml.cs` |
| S1 | Open/save project workflows not fully delegated | `MainWindow.xaml.cs`; project open/save handlers |
| S2 | MainWindow.xaml.cs ~2400 lines across partials; further reduction possible | `MAINWINDOW_DECOMPOSITION_PLAN.md` |

---

## 3. Transport

| Aspect | Status | Evidence |
|--------|--------|----------|
| Transport ownership | ✅ Implemented | `IContextManager.SetCurrentPlayable`; last-writer-wins rules |
| Panel publishers | ✅ Audited | [TRANSPORT_PANEL_PUBLISHERS](TRANSPORT_PANEL_PUBLISHERS.md) — Library, Timeline, Recording, Synthesis, Analyzer |
| Global transport orchestration | ✅ Implemented | `GlobalTransportOrchestrator`, `TransportOrchestrationBootstrap` |
| Regression tests | ✅ Implemented | `ContextManagerTests.cs` — 11 transport ownership tests |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Timeline does not clear on stop; ownership remains until another panel sets | `TRANSPORT_PANEL_PUBLISHERS.md` — intentional; user selects Library to reclaim |

---

## 4. Panel Lifecycle

| Aspect | Status | Evidence |
|--------|--------|----------|
| IPanelLifecycle | ✅ Implemented | `OnActivatedAsync`, `OnDeactivatedAsync`; disposal in deactivation |
| Unsubscribe audit | ✅ Implemented | [PANEL_WIRING_CATALOG](PANEL_WIRING_CATALOG.md) — "No Unsubscribe" gaps closed |
| Disposal tests | ✅ Implemented | Top 5 high-risk panels (Library, Timeline, VoiceSynthesis, EffectsMixer, Recording) |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Training WebSocket events; EventAggregator unsubscribe verified; WebSocket lifecycle separate | `PANEL_WIRING_CATALOG.md` § Training |

---

## 5. Event Wiring

| Aspect | Status | Evidence |
|--------|--------|----------|
| EventAggregator | ✅ Implemented | All inter-panel communication via EventAggregator |
| High-frequency throttle | ✅ Implemented | [HIGH_FREQUENCY_EVENT_THROTTLE_POLICY](HIGH_FREQUENCY_EVENT_THROTTLE_POLICY.md) — PlaybackStateChangedEvent, TimelineSelectionChangedEvent, ScrollSyncEvent, SelectionBroadcastEvent |
| ThrottledEventPublisher | ✅ Implemented | `ThrottledEventPublisher` in `AppServices`; trailing mode 50–100ms |
| Debounce | ✅ Implemented | Library search 300ms debounce |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Throttle instrumentation (dev/smoke counters) optional; not in CI | `ThrottledEventPublisher.GetStats()` |

---

## 6. Backend Seam

| Aspect | Status | Evidence |
|--------|--------|----------|
| IBackendClient migration | ✅ Complete | [IBACKENDCLIENT_UNRESOLVED_QUEUE](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) — routine queue closed; EffectsMixerViewModel migrated |
| Seam maturity | ✅ Documented | [SEAM_MATURITY_AUDIT](SEAM_MATURITY_AUDIT.md) — 40+ seams classified |
| Domain clients | ✅ Implemented | IEffectsMeterClient, IEffectChainClient, IMixerStateClient, etc. |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Naming mismatches: ABTestService, TimelineClipService — "Service" implies policy; pure pass-through | `SEAM_MATURITY_AUDIT.md` § Naming Mismatches |

---

## 7. User Workflow Continuity

| Aspect | Status | Evidence |
|--------|--------|----------|
| Cross-panel workflow proofs | ✅ Implemented | `tests/integration/test_cross_panel_workflows.py` — 7 API-based workflow tests |
| Smoke workflow tests | ✅ Implemented | `tests/ui/test_smoke_workflows.py` — Library asset selection → playback, Timeline play/stop |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S1 | Synthesis → asset → timeline → playback: automated proof exists; E2E with real UI not yet in CI | `test_cross_panel_workflows.py`; `test_smoke_workflows.py` |
| S1 | Profile selection → synthesis coherence: event flow verified; no dedicated end-to-end proof | `PANEL_WIRING_CATALOG.md` |
| S2 | Search result → panel open → selection focus: workflow not fully automated | Manual |

---

## 8. Discoverability / Error / Recovery UX

| Aspect | Status | Evidence |
|--------|--------|----------|
| ErrorDialogService | ✅ Implemented | Centralized error surfacing; XamlRoot-safe |
| Startup overlay | ✅ Implemented | Full-window overlay until BackendReady or BackendFailed; Retry button |
| GracefulDegradationService | ✅ Implemented | 429/502/503/504 → InfoBar; no toast spray |
| ToastNotificationService | ✅ Implemented | Suppresses when degraded |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S1 | Error messages in some panels may not be actionable (e.g. "Backend unavailable" without next steps) | Various ViewModels |
| S2 | Help overlay / onboarding hints not fully integrated with error recovery | `HelpOverlay`, `OnboardingHints` |

---

## 9. Feature Islands vs Integrated Workflows

| Aspect | Status | Evidence |
|--------|--------|----------|
| Core workflow integration | ✅ Partial | Library ↔ Timeline ↔ Synthesis ↔ Profiles ↔ Recording — events wired |
| Module panels | ⚠️ Partial | VoiceCloningWizard, MultiVoiceGenerator, etc. — subscribe to events; integration depth varies |

**Gaps:**

| Severity | Gap | Location |
|----------|-----|----------|
| S2 | Some advanced panels (e.g. ImageGen, VideoGen, DeepfakeCreator) may feel isolated from core synthesis flow | Module panels |
| S2 | Batch processing → workflow automation: integration not fully documented | `PANEL_WIRING_CATALOG.md` |

---

## 10. Premium Gaps by Severity

### S0 (Critical — Blocking)

*None identified.*

### S1 (High — Should Prioritize)

| Gap | Section | Mitigation |
|-----|---------|------------|
| Search overlay / toolbar glue in MainWindow | Shell | Extract to coordinator or service |
| Open/save project workflows not fully delegated | Shell | Move to shell workflow service |
| Synthesis → timeline → playback E2E not in CI | Workflow | Add E2E smoke or gate |
| Profile selection → synthesis coherence: no dedicated proof | Workflow | Add workflow proof |
| Error messages not always actionable | UX | Audit error strings; add recovery hints |

### S2 (Medium — Backlog)

| Gap | Section | Mitigation |
|-----|---------|------------|
| Full verify.ps1 not in CI; manual run | Startup | Consider staged CI or nightly full verify |
| First-launch timeout 45–60s | Startup | Document; consider progress indicator |
| MainWindow size (~2400 lines) | Shell | Continue decomposition per plan |
| Timeline ownership on stop | Transport | Documented; user reclaims via Library |
| Throttle instrumentation optional | Event Wiring | Add to smoke or dev build |
| Naming mismatches (ABTestService, etc.) | Backend | Rename to *Client |
| Search → panel → focus not automated | Workflow | Add smoke if high-value |
| Help overlay / error recovery | UX | Integrate |
| Feature islands (ImageGen, etc.) | Workflows | Document integration depth |

---

## References

| Document | Purpose |
|----------|---------|
| [PANEL_WIRING_CATALOG](PANEL_WIRING_CATALOG.md) | Publish/subscribe, throttle, unsubscribe |
| [MAINWINDOW_DECOMPOSITION_PLAN](MAINWINDOW_DECOMPOSITION_PLAN.md) | Shell extraction status |
| [TRANSPORT_PANEL_PUBLISHERS](TRANSPORT_PANEL_PUBLISHERS.md) | Transport ownership audit |
| [STARTUP_ORCHESTRATION_HARDENING_PLAN](STARTUP_ORCHESTRATION_HARDENING_PLAN.md) | Startup phases |
| [INSTALLED_LAYOUT_REFERENCE](INSTALLED_LAYOUT_REFERENCE.md) | Packaged tree |
| [HIGH_FREQUENCY_EVENT_THROTTLE_POLICY](HIGH_FREQUENCY_EVENT_THROTTLE_POLICY.md) | Throttle policy |
| [SEAM_MATURITY_AUDIT](SEAM_MATURITY_AUDIT.md) | Backend seam classification |
| [IBACKENDCLIENT_UNRESOLVED_QUEUE](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) | Migration status |
| [.cursor/STATE.md](../../.cursor/STATE.md) | Active execution truth |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-17 | Initial audit; Premium Reliability Coherence Pass Task 12. |
