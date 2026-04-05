# VoiceStudio Professional Roadmap v3.0

**Version:** 3.0.0  
**Date:** 2026-03-29  
**Owner:** Overseer (Role 0)  
**Status:** Active — canonical product roadmap for professional-grade completion  
**Supersedes (timeline / phase authority):** [VOICESTUDIO_COMPLETION_ROADMAP_V2.md](VOICESTUDIO_COMPLETION_ROADMAP_V2.md), [MASTER_ROADMAP_UNIFIED.md](MASTER_ROADMAP_UNIFIED.md) for *forward-looking* execution. Historical v1.1.0 closure evidence remains valid in those documents.  
**Companion:** [PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md) — surgical gap list with file paths, acceptance criteria, verification.  
**Evidence baseline:** [VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md](../reports/audit/VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md), [PREMIUM_SOFTWARE_COHERENCE_AUDIT.md](../design/PREMIUM_SOFTWARE_COHERENCE_AUDIT.md)

> **For active task execution, see [.cursor/STATE.md](../../.cursor/STATE.md) ACTIVE WINDOW.** This document defines phases, hero workflows, and exit criteria; STATE holds the current lane and proof pointers.

---

## Product identity (one sentence)

**VoiceStudio is the professional desktop workstation for voice cloning, dialogue editing, and speech production.**

Every roadmap decision defers to that sentence. Features that do not strengthen one of the three hero workflows ship after Phases 0–7 or behind an **Advanced / Expert** boundary in UX.

---

## Current state (honest baseline)

| Dimension | Assessment | Source |
|-----------|------------|--------|
| Product wiring (user-visible depth) | ~35–40% professional-complete | Forensic audit 2026-03-28 |
| Architecture / seams | ~80%+ | Audit + governance |
| Governance / CI scaffolding | ~90%+ | Rules, ADRs, verify.ps1 |
| Inner feature integration | Feature islands; partial cross-panel flows | Audit §5.2, coherence audit S1/S2 |

**Truth sync (post-audit):** Lane `GOV-VOICESTUDIO-VOICE-CLONING-INTEGRITY-01` closed 2026-03-29 — reference audio binding for the clone wizard and API fields were addressed; re-verify remaining Hero 1 gaps against [PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md) before treating audit cloning narrative as current blockers.

---

## Permanent CI invariants (carried from Roadmap v2)

These do not sunset with v1.1.0; they remain non-negotiable for merge:

| ID | Invariant |
|----|-----------|
| **I-1** | Router uniqueness — single voice route family; `voice.py` god-route remains deleted |
| **I-2** | Synthesis safety — `require_synthesis_clearance` (or successor) on synthesis endpoints |
| **I-3** | Proof schema — golden path / proof artifacts match enforced schema |
| **I-4** | OpenAPI — concrete response schemas where CI enforces |
| **I-5** | Exception handling — no silent swallow; structured error surface |

---

## Section 1 — Three hero workflows

### Hero 1: Clone a voice and generate production-ready takes

| Aspect | Detail |
|--------|--------|
| **Covers** | Profiles, cloning wizard, synthesis, quality metrics, engine selection, export |
| **Target** | Short reference audio → bound profile → immediate synthesis → production WAV/MP3 with predictable quality |
| **Remaining risks** | Residual binding edge cases; long-form consistency; export paths for server-only assets |

### Hero 2: Edit dialogue on a timeline with pro playback and cleanup

| Aspect | Detail |
|--------|--------|
| **Covers** | Timeline, transport, recording, transcription, effects, metering, waveform editing, normalization |
| **Target** | Multi-track (or staged multi-track) timeline, trustworthy transport, real metering, non-destructive or staged-destructive edits, LUFS-aware export |
| **Remaining risks** | Timeline persistence (global vs per-project), prosody stub, fake/simulated meters, no text-based edit loop |

### Hero 3: Train / manage voice profiles and export clean deliverables

| Aspect | Detail |
|--------|--------|
| **Covers** | Training, datasets, profile quality, batch processing, export presets, job durability |
| **Target** | Real vs simulated training is unmistakable in UX; batch jobs succeed when engines write files; deterministic export presets |
| **Remaining risks** | Simulation fallback; batch `None` vs file path; unified project save; durable job queue |

---

## Phase 0 — Stop the bleeding (Weeks 1–2)

**Goal:** No shipped lie: broken endpoints, fake telemetry, and Gate C blockers are removed or gated.

**Entry criteria:** `.\scripts\verify.ps1 -Quick` GREEN on branch.  
**Exit criteria:** All items below verified; tracker rows closed or explicitly deferred with ADR/ledger.

| ID | Deliverable | Primary evidence |
|----|-------------|------------------|
| F0-1 | Voice cloning reference path end-to-end (verify post–cloning-integrity lane) | `backend/services/profile_service.py`, wizard routes, tests |
| F0-2 | Batch processing handles `output_path` + `None` return like synthesis | `backend/api/routes/batch.py` |
| F0-3 | Engine telemetry: no fake success metrics on failure | `backend/api/routes/engine.py` |
| F0-4 | Single canonical synthesis execution path (document + enforce) | `backend/services/synthesis_service.py` vs `backend/voice/services/` |
| F0-5 | Remove or quarantine orphan `Features/*` ViewModels (duplicate synthesis/timeline) | `src/VoiceStudio.App/Features/` |
| F0-6 | Resolve ADR-045 / ADR-049 MCP numbering collision | `docs/architecture/decisions/` |
| F0-7 | Fix PanelHost `ContentProperty` shadow (Gate C / XAML safety) | `src/VoiceStudio.App/Controls/PanelHost.xaml.cs` |

**Milestone:** ~42% product depth (broken seams closed). Hero 1 trustworthy for clone→synthesize after F0-1 verification.

---

## Phase 1 — Shell decomposition and quality gates (Weeks 3–5)

**Goal:** MainWindow stops being the gravity well; transport, selection, and undo become real.

| ID | Deliverable |
|----|-------------|
| F1-1 | Decompose shell: lifecycle, navigation/panels, transport, project/session, dialogs, startup, workspace |
| F1-2 | Timeline (and shell) transport: Record, Loop, time display wired to ViewModel / orchestrator |
| F1-3 | Mica / `SystemBackdrop` on MainWindow where applicable; title bar integration |
| F1-4 | `ISelectionService` (or equivalent) — profile / track / clip selection single source of truth |
| F1-5 | Undo/redo: ScriptEditor, EffectsMixer, Timeline push `IUndoableCommand` |
| F1-6 | PanelHost LRU eviction: `IsActive`, `Dispose`, unsubscribe |
| F1-7 | Remove deprecated `WorkspaceManager` if superseded by `PanelStateService` |
| F1-8 | **Product SLOs** defined + CI hooks (targets): cold launch < 15s; panel switch < 200ms; short synthesis preview < 3s; deterministic export hash; memory ceiling |

**Milestone:** Shell modular; transport and selection trustworthy; undo visible on core panels.

---

## Phase 2 — Persistence and reliability foundation (Weeks 6–9)

**Goal:** Workstation-grade trust — state survives restart; jobs are durable; resilience is not hand-rolled fiction.

| ID | Deliverable |
|----|-------------|
| F2-1 | SQLite (+ Alembic) for backend authoritative state |
| F2-2 | Timeline state per project / session — not global in-memory only |
| F2-3 | Unified project save: timeline + mixer + profiles + effects + layout (+ synthesis metadata as scoped) |
| F2-4 | Durable job queue (SQLite-backed minimum) replacing fire-and-forget `asyncio.create_task` for batch/training |
| F2-5 | Autosave + crash recovery UX |
| F2-6 | Unify API projects vs `JsonProjectRepository` — one truth or explicit sync contract |
| F2-7 | Polly v8 / `Microsoft.Extensions.Http.Resilience` — retry with jitter, circuit breaker, UI degradation signals |
| F2-8 | Real prosody DSP (replace `audio.copy()` stub) | `backend/api/routes/voice/processing.py` |
| F2-9 | Training simulation: blocking UX + status honesty (no silent “complete” for sim) |

**Milestone:** ~60% product depth; sessions and projects survive process death; backend HTTP honest under failure.

---

## Phase 3 — Cross-feature wiring (Weeks 10–13)

**Goal:** Compounding functionality — panels behave as one product.

| ID | Deliverable |
|----|-------------|
| F3-1 | Synthesis → Timeline: add clip at playhead / one-click handoff |
| F3-2 | Clone → Profile → Synthesis: verified E2E (events + selection) |
| F3-3 | Recording → Library → Timeline: discoverable path (drag/drop or command) |
| F3-4 | Training complete → profile quality / metadata update pipeline |
| F3-5 | Effects → Export: bake chain in export path |
| F3-6 | Batch → Quality dashboard: data flow |
| F3-7 | Timeline mixdown → master → export |
| F3-8 | Library → panels: DnD and context actions |
| F3-9 | Transcription ↔ clip linkage (prep for Phase 5) |
| F3-10 | Global profile selection sync across consumers |
| F3-11 | OS notifications for high-priority job completion / failures |
| F3-12 | NAudio default device change handling (`MMDeviceEnumerator` / equivalent) |

**Milestone:** Hero 1 and 3 feel like one app; Hero 2 prepped for audio depth in Phase 4.

---

## Phase 4 — Professional audio engine (Weeks 14–19)

**Goal:** DAW-class audio operations for dialogue work.

| ID | Deliverable |
|----|-------------|
| F4-1 | Live metering (VU / LUFS / true peak) — not stored-only or simulated-only in production UI |
| F4-2 | Waveform editing: cut, copy, paste, fade, crossfade |
| F4-3 | GPU path for waveform/spectrogram (e.g. Win2D `CanvasAnimatedControl`) where beneficial |
| F4-4 | Real-time effects preview + bypass |
| F4-5 | Non-destructive edit model (or staged commits with clear UX) |
| F4-6 | LUFS normalization on export (-16 podcast / -23 broadcast presets) |
| F4-7 | Multitrack recording (staged: arm multiple inputs) |
| F4-8 | In-app model download manager (progress, verify, resume) |
| F4-9 | Design token audit — eliminate conflicting `VSQ.*` keys across themes |
| F4-10 | NuGet alignment: CommunityToolkit.Mvvm, Win2D, CommunityToolkit.WinUI per matrix |

**Milestone:** ~75% depth; Hero 2 competitive with Audition-class *core* dialogue tasks (not full DAW).

---

## Phase 5 — AI differentiation (Weeks 20–25)

**Goal:** Moat features — text-first editing, regeneration, cleanup.

| ID | Deliverable |
|----|-------------|
| F5-1 | Text-based audio editing (transcript edit → audio update) |
| F5-2 | AI regenerate segment |
| F5-3 | Filler word detection / removal |
| F5-4 | One-click “Studio Sound” cleanup chain |
| F5-5 | Long-form voice consistency strategy |
| F5-6 | Emotional control with preview |
| F5-7 | Speech-to-speech pipeline (scoped) |
| F5-8 | Engine benchmark UI (MOS / side-by-side) |
| F5-9 | User-configurable engine priority (replace static fallback list) |
| F5-10 | SSML: engine capability detection + user-visible strip/warn |

**Milestone:** ~85% depth; competitive story vs Descript + local-first ElevenLabs-class cloning.

---

## Phase 6 — Security, compliance, enterprise (Weeks 26–29)

| ID | Deliverable |
|----|-------------|
| F6-1 | Voice consent capture + storage + audit |
| F6-2 | Optional watermarking / provenance (align [PROVENANCE_POLICY.md](PROVENANCE_POLICY.md)) |
| F6-3 | Auth on by default for non-localhost deployments |
| F6-4 | WebSocket auth parity with HTTP |
| F6-5 | Audit trail: synthesis / clone / export |
| F6-6 | Model version provenance on outputs |
| F6-7 | RBAC (multi-user future) |
| F6-8 | Chatterbox vs torch pin — dual venv or upgrade path (TD-001 class) |

---

## Phase 7 — UX professional polish (Weeks 30–33)

| ID | Deliverable |
|----|-------------|
| F7-1 | First-run: models, GPU, keys |
| F7-2 | Skeleton / shimmer loading |
| F7-3 | Actionable errors (no raw status codes in UI) |
| F7-4 | Shortcut registry + conflicts |
| F7-5 | Theme tokens + contrast |
| F7-6 | Responsive panel minimum sizes |
| F7-7 | Contextual help + tooltips |
| F7-8 | Notification center |
| F7-9 | Jump lists + `.vstudio` association (installer) |
| F7-10 | Taskbar progress for long jobs |
| F7-11 | Essentials / Advanced / Expert IA |
| F7-12 | WCAG 2.1 AA pass |
| F7-13 | Cold start < 10s (stretch goal; measure per SLO) |

**Milestone:** ~95% professional polish on hero paths.

---

## Phase 8+ — Post–hero path (explicitly deferred)

**Do not start until Phases 0–7 exit criteria are met** (or Overseer-approved exception with ADR).

| ID | Area |
|----|------|
| F8-1 | VST3 / CLAP hosting |
| F8-2 | Public API docs + SDK |
| F8-3 | Voice marketplace |
| F8-4 | Auto captions |
| F8-5 | Video-forward workflows |
| F8-6 | Dubbing / localization at scale |
| F8-7 | Spectral editing |
| F8-8 | Image / video engine surfaces (breadth reduction until heroes win) |
| F8-9 | Real-time collaboration |
| F8-10 | Mobile companion |

---

## Continuous track — CI, testing, operations (parallel)

| Stream | Objective |
|--------|-----------|
| **C-1** | C# skip debt: 274 → < 50 with documented reasons ([SKIP_DEBT_CLEANUP_SUBPLAN.md](../design/SKIP_DEBT_CLEANUP_SUBPLAN.md)) |
| **C-2** | Full `verify.ps1` in GitHub Actions (not local-only) |
| **C-3** | Real-engine golden path in CI (policy decision: [GOLDEN_PATH_PROOF_STATUS.md](../reports/verification/GOLDEN_PATH_PROOF_STATUS.md)) |
| **C-4** | Mypy strict burn-down ([STRICT_MYPY_BURNDOWN_SUBPLAN.md](../design/STRICT_MYPY_BURNDOWN_SUBPLAN.md)) |
| **C-5** | Workflow deduplication ([WORKFLOW_CONSOLIDATION_SUBPLAN.md](../design/WORKFLOW_CONSOLIDATION_SUBPLAN.md)) |
| **C-6** | E2E tests per hero workflow |
| **C-7** | Performance regression suite (SLO enforcement) |
| **C-8** | Security scanning (pip-audit, dotnet vulnerable, Bandit policy) |
| **C-9** | Git hygiene: push cadence, clean tree, no committed `TestResults/` noise |
| **C-10** | Backend surface simplification: `deps.py` / `dependencies.py`, rate limit variants → documented canonical modules |
| **C-11** | Repo hygiene: `.gitignore` for venv/cache/bin/obj; single blessed bootstrap doc |
| **C-12** | Documentation truth: README license = LICENSE; [PROJECT_STATUS_EMBEDDED.md](../../PROJECT_STATUS_EMBEDDED.md) vs reality |
| **C-13** | Plugin SDK hardening: permissions, crash containment, certification harness ([PLUGIN_SYSTEM_GUIDELINES.md](PLUGIN_SYSTEM_GUIDELINES.md), ADR-036–040) |

---

## Milestone summary

| After phase | Approx. product depth | Hero emphasis |
|-------------|------------------------|---------------|
| Phase 0 | ~42% | Hero 1 integrity |
| Phase 2 | ~60% | Trust + persistence |
| Phase 4 | ~75% | Hero 2 audio |
| Phase 5 | ~85% | AI moat |
| Phase 7 | ~95% | Enterprise + polish |

---

## Design principles (ruthless)

1. **Phase 0 before shell vanity** — A pretty Mica window that clones into a broken profile is still trash.  
2. **Persistence before wiring** — Wiring islands on global JSON/memory is building on sand.  
3. **Audio primitives before AI text edit** — Regenerate needs splice, fade, and loudness discipline.  
4. **Security after honesty** — Compliance without functional truth is compliance theater.  
5. **Breadth last** — VST, marketplace, and multimodal breadth are rewards for hero-path victory, not distractions from it.

---

## Related documents

| Document | Role |
|----------|------|
| [PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md) | Executable gap IDs |
| [VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md](../reports/audit/VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md) | Forensic evidence |
| [ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md](ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md) | Task backlog reference — reconcile to V3 phases when picking work |
| [MASTER_ROADMAP_UNIFIED.md](MASTER_ROADMAP_UNIFIED.md) | Historical Quantum+ milestones — **forward phase authority superseded by this V3 doc** |
| [DEFERRED_V1_2.md](DEFERRED_V1_2.md) | Advisory items absorbed into Continuous track |
| [POST_EXTRACTION_TRANSITION_PLAN.md](../design/POST_EXTRACTION_TRANSITION_PLAN.md) | Workflow coherence — aligns with Phase 3 |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-29 | v3.0.0 initial publication; supersedes forward-looking timeline in Completion Roadmap v2 and Unified Master Roadmap |
