# Professional Gap Tracker

**Companion:** [VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md](../governance/VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md) — **Last tracker sync:** 2026-03-28 (GAP-011 closed; **GAP-009 Closed** — [Transport Authority lane closure](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md); next execution lane: **Persistence Foundation** `GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01`)  
**Sources (merged + deduplicated):** [VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md](../reports/audit/VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md), [PREMIUM_SOFTWARE_COHERENCE_AUDIT.md](PREMIUM_SOFTWARE_COHERENCE_AUDIT.md), Desktop Commander technical gap list (transport, PanelHost, selection, SSML, undo, layout, HttpClient, GPU waveform, design tokens, Mica, jump lists, NAudio, circuit breaker, WorkspaceManager, notifications, NuGet), strategic 10-point vision (shell SLOs, backend surface, UX hierarchy, repo hygiene, docs truth).

**Dedup rule:** One row per unique defect/capability. Overlaps (e.g. transport bar in audit + shell list) map to a single **GAP-ID**.

**Status values:** `Open` | `In Progress` | `Closed` | `Superseded` (link proof or ADR).

---

## Master index

**Count:** **69** deduplicated gaps (within the plan’s ~65–70 target). Phase 0–6 and execution slices for Phases 4–5 use **GAP-001–062**; Phase 7 shell/UX is **GAP-063–067**; post–hero and CI tracks are **GAP-068–069**. *Backend `deps.py` / `dependencies.py` split is folded into GAP-069.*

| ID | Phase | Category | Title | Primary file(s) | Effort (h) | Role | Deps | Status |
|----|-------|----------|-------|-----------------|------------|------|------|--------|
| GAP-001 | 0 | Broken | Clone wizard → profile reference audio E2E (re-verify post-lane) | `backend/services/profile_service.py`, `backend/api/routes/*wizard*`, tests | 4 | Core Platform | — | Closed — `GOV-VOICESTUDIO-VOICE-CLONING-INTEGRITY-01` + `VOICESTUDIO_VOICE_CLONING_INTEGRITY_LANE_CLOSURE_2026-03-29.md` |
| GAP-002 | 0 | Broken | Batch job `output_path` / `None` handling parity with synthesis | `backend/api/routes/batch.py` | 6 | Core Platform | — | Closed — `GOV-VOICESTUDIO-RUNTIME-HONESTY-01` + `test_batch_output_path_honesty.py`; Windows `C:\` output paths allowed via `_client_output_path_is_forbidden` |
| GAP-003 | 0 | Broken | Engine route: no fake success telemetry on failure | `backend/api/routes/engine.py` | 4 | Core Platform | — | Closed — `GOV-VOICESTUDIO-RUNTIME-HONESTY-01` + `test_engine_telemetry_honesty.py` |
| GAP-004 | 0 | Missing | Single canonical synthesis execution path (doc + code) | `backend/services/synthesis_service.py`, `backend/voice/services/` | 16 | System Architect | — | Open |
| GAP-005 | 0 | Broken | Orphan / duplicate `Features/*` ViewModels vs canonical panels | `src/VoiceStudio.App/Features/` | 12 | UI Engineer | GAP-004 | Open |
| GAP-006 | 0 | Ops | ADR numbering collision (MCP / integration ADRs) | `docs/architecture/decisions/` | 2 | System Architect | — | Open |
| GAP-007 | 0 | Broken | PanelHost `ContentProperty` shadows `UserControl` (Gate C / XAML) | `src/VoiceStudio.App/Controls/PanelHost.xaml.cs` (~L45) | 8 | UI Engineer | — | Open |
| GAP-008 | 1 | Missing | MainWindow decomposition (lifecycle, nav, transport, session, dialogs) | `src/VoiceStudio.App/Views/MainWindow.xaml(.cs)` | 80 | UI Engineer | GAP-007 | Open |
| GAP-009 | 1 | Wiring | Transport bar: Record, Loop, time ↔ orchestrator + Timeline | `TimelineView.xaml`, `TimelineViewModel.cs`, `GlobalTransportOrchestrator.cs`, `GlobalTransportControl.*`, `TransportShortcutCoordinator.cs`, `MainWindow`; lane **GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01** — [GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md) | 24 | UI Engineer | GAP-008 | Closed — [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md); Slices 1–4 complete; Persistence Foundation unblocked |
| GAP-010 | 1 | UX | Mica / `SystemBackdrop` + title bar integration | `MainWindow.xaml` | 8 | UI Engineer | GAP-008 | Open |
| GAP-011 | 1 | Missing | Cross-panel selection service (profile / track / clip) | `IContextManager` timeline fields + `ProfileSelectedEvent` authority; optional `ISelectionService` deferred | 40 | Core Platform | — | Closed — `GOV-VOICESTUDIO-SELECTION-AUTHORITY-01` + `VOICESTUDIO_SELECTION_AUTHORITY_LANE_CLOSURE_2026-03-28.md` |
| GAP-012 | 1 | Wiring | Undo/redo wired: ScriptEditor, EffectsMixer, Timeline | Respective ViewModels, `IUndoableCommand` | 32 | UI Engineer | GAP-011 | Open |
| GAP-013 | 1 | Broken | PanelHost LRU eviction: dispose, events, `IsActive` | `PanelHost.xaml.cs`, `PanelStateService` | 16 | UI Engineer | GAP-007 | Open |
| GAP-014 | 1 | Ops | Remove deprecated `WorkspaceManager` after `PanelStateService` parity | `WorkspaceManager.cs`, callers | 8 | UI Engineer | GAP-013 | Open |
| GAP-015 | 1 | Ops | Product SLO definitions + CI measurement hooks | `scripts/verify.ps1`, perf tests, docs | 24 | Build Tooling | — | Open |
| GAP-016 | 2 | Missing | SQLite + Alembic for backend authoritative state | `backend/`, migrations | 60 | Core Platform | — | Open |
| GAP-017 | 2 | Missing | Timeline state persisted per project (not global memory only) | Timeline backend + UI sync | 48 | Core Platform | GAP-016 | Open |
| GAP-018 | 2 | Missing | Unified project save: mixer + timeline + layout + scoped synthesis meta | Project services, repos | 56 | Core Platform | GAP-016, GAP-017 | Open |
| GAP-019 | 2 | Missing | Durable job queue (SQLite-backed) for batch/training/export | `backend/services/`, task spawn sites | 40 | Core Platform | GAP-016 | Open |
| GAP-020 | 2 | Missing | Session autosave + crash recovery UX | App + backend | 32 | UI Engineer | GAP-018 | Open |
| GAP-021 | 2 | Broken | Unify API project model vs `JsonProjectRepository` | `JsonProjectRepository`, API routes | 24 | Core Platform | GAP-016 | Open |
| GAP-022 | 2 | Missing | Replace hand-rolled resilience with Polly v8 / `HttpClient` resilience | `BackendClient.cs`, DI | 24 | Core Platform | — | Open |
| GAP-023 | 2 | Broken | Prosody: real time-stretch / pitch vs `audio.copy()` stub | `backend/api/routes/voice/processing.py` | 32 | Engine Engineer | — | Open |
| GAP-024 | 2 | Broken | Training simulation: modal + block “complete” on simulated runs | `TrainingViewModel`, backend training routes | 16 | UI Engineer | — | Open |
| GAP-025 | 3 | Wiring | Synthesis → Timeline: add clip at playhead / handoff command | Synthesis + Timeline VMs | 16 | UI Engineer | GAP-011 | Open |
| GAP-026 | 3 | Wiring | Clone → Profile → Synthesis: event + selection E2E | Multiple panels | 24 | UI Engineer | GAP-001, GAP-011 | Open |
| GAP-027 | 3 | Wiring | Recording → Library → Timeline discoverable path | Library, Recording, Timeline | 20 | UI Engineer | GAP-011 | Open |
| GAP-028 | 3 | Wiring | Training complete → profile quality / metadata refresh | Training + Profiles | 16 | Core Platform | GAP-024 | Open |
| GAP-029 | 3 | Wiring | Effects chain baked into export path | EffectsMixer, export service | 24 | Core Platform | — | Open |
| GAP-030 | 3 | Wiring | Batch results → quality dashboard | Batch routes, dashboard VM | 12 | UI Engineer | GAP-002 | Open |
| GAP-031 | 3 | Wiring | Timeline multi-track mixdown → master → export | Timeline, export | 32 | Core Platform | GAP-017 | Open |
| GAP-032 | 3 | Wiring | Library drag-drop / context actions to all relevant panels | `LibraryPanel`, DnD | 24 | UI Engineer | GAP-011 | Open |
| GAP-033 | 3 | Wiring | Transcription text linked to timeline clips | Transcription, Timeline | 28 | Core Platform | GAP-017 | Open |
| GAP-034 | 3 | UX | OS-level notifications for training/batch/export completion | WinUI AppNotifications / equivalent | 16 | UI Engineer | — | Open |
| GAP-035 | 3 | Broken | NAudio default device change subscription | Audio device layer | 12 | Core Platform | — | Open |
| GAP-036 | 4 | Missing | Real-time metering (VU / LUFS / true peak) in production UI | Meter controls, analysis pipeline | 48 | Engine Engineer | — | Open |
| GAP-037 | 4 | Missing | Waveform editing: cut, copy, paste, fade, crossfade | Timeline / waveform control | 64 | UI Engineer | GAP-017 | Open |
| GAP-038 | 4 | Missing | GPU waveform / spectrogram rendering path | Win2D / `CanvasAnimatedControl` | 40 | UI Engineer | — | Open |
| GAP-039 | 4 | Missing | Real-time effects preview + bypass | Effects engine + NAudio graph | 48 | Engine Engineer | — | Open |
| GAP-040 | 4 | Missing | Non-destructive edit model + deep undo | Timeline domain | 56 | System Architect | GAP-012 | Open |
| GAP-041 | 4 | Missing | LUFS normalization presets on export (-16 / -23) | Export pipeline | 24 | Engine Engineer | GAP-029 | Open |
| GAP-042 | 4 | Missing | Multi-track recording (arm / route multiple inputs) | Recording panel, drivers | 40 | Core Platform | GAP-035 | Open |
| GAP-043 | 4 | Missing | In-app model download manager (progress, verify, resume) | New UI + backend | 48 | Release Engineer | — | Open |
| GAP-044 | 4 | Ops | Design tokens (`VSQ.*`) + NuGet alignment (CommunityToolkit.Mvvm, Win2D, CommunityToolkit.WinUI) | `Themes/*.xaml`, `*.csproj` | 24 | UI Engineer / Build | GAP-038 | Open |
| GAP-045 | 5 | AI | Text-based audio editing (transcript → edit → regen) | Transcription + synthesis | 80 | Engine Engineer | GAP-033, GAP-023 | Open |
| GAP-046 | 5 | AI | AI regenerate segment without full re-record | Pipeline + UI | 48 | Engine Engineer | GAP-045 | Open |
| GAP-047 | 5 | AI | Filler word detection + removal | Analysis + Timeline | 40 | Engine Engineer | GAP-045 | Open |
| GAP-048 | 5 | AI | One-click “Studio Sound” (NR + enhance + norm) | Effects presets | 32 | Engine Engineer | GAP-039 | Open |
| GAP-049 | 5 | AI | Long-form voice consistency strategy | Engine + profile | 40 | Engine Engineer | — | Open |
| GAP-050 | 5 | AI | Emotional voice control + preview | Emotion domain | 48 | Engine Engineer | — | Open |
| GAP-051 | 5 | AI | Speech-to-speech conversion path (scoped) | `backend/voice/` | 64 | Engine Engineer | — | Open |
| GAP-052 | 5 | UX | Engine benchmarking UI (MOS / side-by-side) | Benchmarks panel | 32 | UI Engineer | — | Open |
| GAP-053 | 5 | Missing | User-configurable engine priority (no static fallback chain) | Settings + router | 24 | System Architect | — | Open |
| GAP-054 | 5 | Wiring | SSML: capability detect + strip/warn per engine | Synthesis request path | 16 | Core Platform | GAP-053 | Open |
| GAP-055 | 6 | Security | Voice consent capture + storage + audit | Legal UX + DB | 40 | System Architect | GAP-016 | Open |
| GAP-056 | 6 | Security | Audio watermarking / provenance (policy-aligned) | Export pipeline | 48 | Engine Engineer | [PROVENANCE_POLICY.md](../governance/PROVENANCE_POLICY.md) | Open |
| GAP-057 | 6 | Security | Mandatory auth for non-localhost deployments | `auth_middleware.py`, config | 32 | Core Platform | — | Open |
| GAP-058 | 6 | Security | WebSocket authentication parity with HTTP | WS stack | 24 | Core Platform | GAP-057 | Open |
| GAP-059 | 6 | Security | Audit trail: who / what / when / which model | Logging + persistence | 40 | Core Platform | GAP-016 | Open |
| GAP-060 | 6 | Security | Model version provenance on outputs | Metadata in exports | 24 | Core Platform | GAP-059 | Open |
| GAP-061 | 6 | Security | RBAC groundwork for multi-user | Middleware + roles | 48 | System Architect | GAP-057 | Open |
| GAP-062 | 6 | Ops | Chatterbox vs pinned torch — dual venv or upgrade | `engines/`, env docs | 24 | Engine Engineer | TD-001 class | Open |
| GAP-063 | 7 | UX | First-run wizard (models, GPU, keys) | Onboarding flow | 32 | UI Engineer | GAP-043 | Open |
| GAP-064 | 7 | UX | Skeleton loading + actionable errors (no raw HTTP codes) | Global error + panels | 24 | UI Engineer | — | Open |
| GAP-065 | 7 | UX | Shortcut registry + conflicts + customization | Input service | 32 | UI Engineer | — | Open |
| GAP-066 | 7 | UX | Theme / responsive min sizes / contextual help | Themes + layout | 40 | UI Engineer | GAP-044 | Open |
| GAP-067 | 7 | UX | Notification center + jump lists + `.vstudio` assoc. + taskbar progress + progressive disclosure + WCAG 2.1 AA + cold-start (<10s stretch) | Shell, installer, all panels, startup | 192 | UI Engineer / Release / Core | GAP-034, GAP-018, GAP-015 | Open |
| GAP-068 | 8+ | Ecosystem | **Post–hero:** VST3/CLAP feasibility; public API + SDK; marketplace (consent-gated); captions; dubbing; video workflow; spectral editing; collaboration; mobile companion | Multiple future surfaces | 1040 | System Architect | Phases 0–7 exit | Open |
| GAP-069 | Cont | Ops | **Continuous:** C# skip debt; full `verify.ps1` in GHA; golden path / E2E heroes / perf SLO tests; mypy burn-down; workflow consolidation; `.gitignore` + doc truth; pip-audit / dotnet vuln; plugin SDK hardening; backend `deps.py`/`dependencies.py` consolidation | `.github/`, `tests/`, root docs, `backend/api/` | 408 | Build Tooling / Overseer / Architect | GAP-015, GAP-026 | Open |

---

## Verification commands (typical)

| Gap class | Command / check |
|-----------|-----------------|
| Backend route / service | `python -m pytest tests/unit/backend/ -q` + targeted file |
| UI / shell | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` + `dotnet test src/VoiceStudio.App.Tests/...` |
| Full gate | `.\scripts\verify.ps1 -Quick` (pre-commit); `.\scripts\verify.ps1` (merge) |
| Contract | `python -m pytest tests/contract/ -q` |
| Panel / automation | WinAppDriver suite; `docs/developer/AUTOMATION_ID_REGISTRY.md` |

Per-gap **acceptance** is stated below in shorthand; closure requires a proof artifact path in `.cursor/STATE.md` or a verification report under `docs/reports/verification/`.

---

## Detailed entries (Phase 0 — sample full shape)

Use the same shape when promoting any gap to active work (copy template).

### GAP-001 — Clone reference audio E2E

- **Current behavior:** Closed 2026-03-29 — voice cloning integrity lane + binding tests; see closure report under `docs/reports/verification/VOICESTUDIO_VOICE_CLONING_INTEGRITY_LANE_CLOSURE_2026-03-29.md`.
- **Required behavior:** Wizard-selected reference audio persists on profile; API exposes `reference_audio_bound`; synthesis clone uses stored reference without silent fallback.
- **Verification:** `python -m pytest tests/unit/backend/services/test_profile_service_binding.py tests/unit/backend/api/routes/test_wizard_binding.py -q`; manual wizard once; proof in lane closure doc.
- **Role:** Core Platform

### GAP-002 — Batch `None` handling

- **Current behavior:** Closed 2026-03-29 — runtime honesty lane: disk-only engines (`synthesize` returns `None` but writes `output_path`) complete successfully; see `test_batch_output_path_honesty.py`.
- **Required behavior:** Batch jobs always resolve to written artifact path or structured failure; UI shows same class of error as synthesis.
- **Verification:** Unit tests on `batch.py` + integration test with temp output dir.

### GAP-003 — Engine telemetry honesty

- **Current behavior:** Closed 2026-03-29 — failures yield HTTP 503 `TELEMETRY_UNAVAILABLE` instead of placeholder metrics; see `test_engine_telemetry_honesty.py`.
- **Required behavior:** No fabricated telemetry when the engine service or runtime cannot produce real metrics.
- **Verification:** Unit tests + `GET /api/engine/telemetry` contract expectations updated for 503 on failure paths.

### GAP-011 — Cross-panel selection authority

- **Current behavior:** Closed 2026-03-28 — canonical `ProfileSelectedEvent` (no new `VoiceProfileSelectedEvent` publishes); Features `SynthesisViewModel` consumes the same bus as Profiles/Library/workflow; `IContextManager` holds `ActiveTimelinePrimaryClipId` / `ActiveTimelinePrimaryTrackId` with `TimelineViewModel` syncing selection; proof in `docs/reports/verification/VOICESTUDIO_SELECTION_AUTHORITY_LANE_CLOSURE_2026-03-28.md`.
- **Required behavior (original intent):** One place to read “what is selected” for profile and timeline hero path; consumers do not fork parallel event types for the same intent.
- **Deferred:** Optional thin `ISelectionService` facade if mock surface or DI ergonomics require it later.

### GAP-007 — PanelHost ContentProperty

- **Current behavior:** `public static new readonly DependencyProperty ContentProperty` shadows `UserControl.ContentProperty` — compiler/analyzer risk and Gate C failure mode.
- **Required behavior:** Unique DP name or pattern that satisfies XAML compiler and Gate C; no shadowing of framework `ContentProperty` without documented exception.
- **Verification:** `dotnet build` Gate C; `tests/ci` panel drift if applicable.

---

## Role reference

| Role | Typical ownership |
|------|-------------------|
| Overseer | Priority, registry, proof index |
| System Architect | Seams, ADRs, cross-cutting design |
| UI Engineer | WinUI, ViewModels, XAML safety |
| Core Platform | Backend services, persistence, HTTP client |
| Engine Engineer | Engines, DSP, quality metrics |
| Build Tooling | CI, verify.ps1, skip debt |
| Release Engineer | Installer, packaging, OS integrations |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-29 | Initial tracker; 69 deduplicated gaps; companion to Roadmap V3 |
| 2026-03-29 | GAP-001/002/003 closed (cloning integrity + runtime honesty lane) |
| 2026-03-28 | GAP-011 closed (selection authority lane: `ProfileSelectedEvent` + context timeline fields) |
