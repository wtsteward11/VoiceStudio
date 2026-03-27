# Canonical Document Registry

This registry is the single source of truth for all canonical documents in VoiceStudio.
Before creating a new document, check this registry to ensure the topic isn't already covered.

> **Last Updated**: 2026-03-27 (R1A execution row + **STATE** sync) — **Repo-global Quick** ([`latest_pointer.json`](../../artifacts/verify/latest_pointer.json)): **`artifacts/verify/20260326_230934`** (PASS) — **GOV-STASH0-T1-S1-01** **closed**; pointer **`commit_hash`** **`f60477978e72ad3bdbcfb2f2ba7e56c50ebc76c3`** (implementation tree). **`main`** **integrated** **T1-S1** **2026-03-27** — **current `main` `HEAD`**: **`git rev-parse HEAD`** (do **not** treat stale pinned hashes in prose as proof). **`main`** merge history: **`stash0-t1-s1-01`** fast-forward. **`run_verification`** ledger (synced to **STATE** this pass): **`20260327-151108`** (*pre-merge narrative sync **`20260327-145802`***). **T2** Quick **`20260326_211554`** / **`dc07e515`** — **historical** (superseded as repo-global tip by **T1-S1** above). **Prior** authoritative Quick (P05-Persist-A4): **`20260326_163358`** / **`3ad39e35678758eb2903e08713db35b876737c81`** — **historical** unless pointer rewound. **`git rev-parse HEAD` / `main`** may differ after **docs-only** commits — **do not** equate **HEAD** with **`latest_pointer.json`** without checking. **W8-C2** / **W8-C3** / **W8-C1** = **Pass 08 immutable closure** proofs — **`20260326_034012`** / **`0575ea2e`**, **`20260326_025824`** / **`eb986040`**, **`20260325_191036`** / **`bcd6d4e5`** — **historical**; **not** repo-global latest unless pointer matches. **Hermetic compile baseline** **`20260326_020644`** / **`8ba6363f`**. **Workflow 7** — **W7-C1 closed**; **paused** ([Pass 07 §8.4](../design/WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md#84-workflow-7--continuation--pause-governance)) — Quick **`20260325_162114`** / seam **2**. **Product trust Pass 01** [**paused after slice 4** (§8.9)](../design/PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md#89-pass-01-continuation--closure-decision-planning-only). **Prior** global Quick (Pass 06 / pre-A4): **`20260326_145710`** / **`e2819074`**. **Prior** **`20260326_131604`** / **`a7a45f4c`** superseded by **`e2819074`** row. **Pass 06** slices **1–5** — global Quick **`20260326_145710`**; seam **32** + Python **8** (§7.2). **Pass 05 Option A** Quick **`20260325_044801`** / seam **50**; Option **C** **`20260325_031737`** / **27**.

---

## Rules and Governance

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Agent Rules | `.cursor/rules/*.mdc` | 2026-02-19 | 42 files across 8 categories |
| Error Resolution Standard | `.cursor/rules/workflows/error-resolution.mdc` | 2026-02-19 | Mandatory error discovery, logging, and professional resolution standards |
| No Deferral on Encounter | `.cursor/rules/quality/no-deferral-on-encounter.mdc` | 2026-02-19 | Pre-existing issues MUST be fixed when encountered — no kicking the can |
| Human Rules Reference (Legacy) | `docs/archive/legacy_worker_system/governance/MASTER_RULES_COMPLETE.md` | 2026-01-30 | **ARCHIVED** — Legacy worker/overseer rules; use `.cursor/rules/*.mdc` and [ROLE_GUIDES_INDEX](ROLE_GUIDES_INDEX.md) for current governance |
| Rule Proposal Template | `docs/governance/templates/RULE_PROPOSAL_TEMPLATE.md` | — | **DEFERRED** — Template not yet created; low priority |
| Rule Review Checklist | `.cursor/rules/quality/rule-review.mdc` | 2026-01-25 | Quality checklist for rule review |
| Document Governance | `docs/governance/DOCUMENT_GOVERNANCE.md` | 2026-01-30 | File creation and lifecycle; 4-gate check, versioning, archive workflow |
| Archive Policy | `docs/governance/ARCHIVE_POLICY.md` | — | **DEFERRED** — Use [document-lifecycle.mdc](.cursor/rules/workflows/document-lifecycle.mdc) and archive structure in `docs/archive/` |
| Governance Lock | `docs/governance/GOVERNANCE_LOCK.md` | — | **DEFERRED** — Low priority |
| Definition of Done | `docs/governance/DEFINITION_OF_DONE.md` | 2026-01-25 | Consolidated completion criteria |
| Session State | `.cursor/STATE.md` | 2026-03-27 | **Global Quick** **`20260326_230934`** / **`f6047797`** (**T1-S1** §8); **`run_verification`** **`20260327-151108`**; **`main`** carries **T1-S1**; **Active:** **`GOV-STASH0-T1-R1A-EXEC-01`** — **§1** implementation **Pending** (**ten-path** **T1-C2**); **R1** **Option A** **2026-03-27**; **T2** historical; **`stash@{0}`** **parked**; **W7** / Product trust / **W8** paused unless signed reopen. |
| Memory Index | `openmemory.md` | 2026-01-25 | Living project index for AI context |
| **Project Handoff Guide** | `docs/governance/PROJECT_HANDOFF_GUIDE.md` | 2026-01-30 | Maintainer entry point; gate status, build/test, structure, roles, task brief creation |
| **Overseer Newcomer Handoff** | `docs/governance/overseer/OVERSEER_NEWCOMER_HANDOFF.md` | 2026-03-24 | New Overseer (Role 0) onboarding; **§2 project snapshot** (workflow passes, verify pointers, code-truth); Day 1 reads, first commands, daily cadence, non-negotiables |
| **Tech Debt Register** | `docs/governance/TECH_DEBT_REGISTER.md` | 2026-01-29 | Consolidated technical debt, limitations, and future enhancements; categorized by priority (High/Medium/Low) |
| **Production Readiness Statement** | `docs/PRODUCTION_READINESS.md` | 2026-01-30 | Formal production readiness declaration for v1.0.0 BASELINE; capabilities, limitations, quality gates, support |
| Task Brief System | `docs/tasks/README.md` | 2026-01-30 | Task brief workflow and conventions; lifecycle: Analyze → Blueprint → Construct → Validate |
| Task Brief Template | `docs/tasks/TASK_TEMPLATE.md` | 2026-01-30 | Standard task brief template; new briefs: use next ID (e.g. TASK-0023) per [PROJECT_HANDOFF_GUIDE.md](PROJECT_HANDOFF_GUIDE.md) § Task brief creation |
| Prompt Library | `.cursor/commands/` | 2026-01-25 | Reusable AI prompts and roles |
| Completion Evidence Guard | `tools/overseer/verification/completion_guard.py` | 2026-02-01 | Prevents completion markers in uncommitted changes; integrated with verification and stop hook |
| **Compatibility Matrix** | `config/compatibility_matrix.yml` | 2026-02-02 | Centralized version pins, dependency constraints, protected surfaces; validated by `scripts/check_compatibility_matrix.py` |
| **CODEOWNERS** | `.github/CODEOWNERS` | 2026-02-02 | Protected surface ownership mapping for PR review auto-assignment |
| **AI Agent Safety Rule** | `.cursor/rules/workflows/auto-mode-safety.mdc` | 2026-02-02 | Mandatory scaffolding, matrix checks, protected surface handling for AI agents |
| **Branch Merge Policy** | `docs/governance/BRANCH_MERGE_POLICY.md` | 2026-02-02 | Divergence limits, branch lifecycle, merge strategies; closes TD-010 |
| **Change Control Rules** | `docs/governance/CHANGE_CONTROL_RULES.md` | 2026-02-09 | Non-negotiable verification gate, stabilization protocol, Cursor agent operating protocol, blast radius limits |
| **Verification Harness Rule** | `.cursor/rules/workflows/verification-harness.mdc` | 2026-02-09 | Agent rule for verify.ps1 usage; "no green = no merge" enforcement |
| **Plugin System Guidelines** | `docs/governance/PLUGIN_SYSTEM_GUIDELINES.md` | 2026-02-16 | Canonical plugin governance: architecture, security, performance, DX, testing, UI, compatibility, risk, observability (10 sections). Companion to ADR-036. |
| **Provenance Policy** | `docs/governance/PROVENANCE_POLICY.md` | 2026-03-01 | Best-effort provenance and usage recording for audio outputs; do not claim full traceability |
| **Completion Roadmap v2.0** | `docs/governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md` | 2026-03-03 | CI-enforced hardened roadmap for v1.1.0; 7 gaps, 6 phases (0/A/B/C/D/E/F), 5 permanent CI invariants |
| **Feature Catalog Master** | `docs/governance/FEATURE_CATALOG_MASTER.md` | 2026-03-05 | Single canonical feature inventory; 47 panels, API/engine/plugin surface; machine appendix: [FEATURE_CATALOG_MASTER.appendix.json](FEATURE_CATALOG_MASTER.appendix.json); CI drift check in `tests/ci/test_feature_catalog_appendix.py` |
| **Finish Line: Personal Studio** | `docs/governance/FINISH_LINE_PERSONAL_STUDIO.md` | 2026-03-06 | Acceptance criteria for workspaces CRUD/import/export, tool catalog, docking/resize/collapse persistence, restore failure recovery; manual thrash test; build determinism rule |
| **Test Classification** | `docs/governance/TEST_CLASSIFICATION.md` | 2026-03-12 | Seam-aware vs transport-mock vs legacy; supports architectural completion claims |
| **Training Lifecycle Async Patterns** | `docs/design/TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md` | 2026-03-12 | Fire-and-forget paths in TrainingViewModel; cancellation ownership |
| **Retained Async Rule** | `docs/design/RETAINED_ASYNC_RULE.md` | 2026-03-13 | Unified rule for ViewModel fire-and-forget; aligns SceneBuilder, BatchProcessing, Training |
| **IBackendClient Unresolved Queue** | `docs/design/IBACKENDCLIENT_UNRESOLVED_QUEUE.md` | 2026-03-13 | Live ranked list of unresolved IBackendClient consumers; use for next migration wave |
| **IBackendClient Inspection Top 3** | `docs/design/IBACKENDCLIENT_INSPECTION_TOP3.md` | 2026-03-13 | File-level inspection for EffectsMixer, TemplateLibrary, VoiceMorph; no migration without sheet |
| **EffectsMixer Domain Split Analysis** | `docs/design/EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md` | 2026-03-13 | Design-before-implementation; three seams (Meter, EffectChain, MixerState); Option C |
| **EffectsMixer Lifecycle Verification** | `docs/design/EFFECTSMIXER_LIFECYCLE_VERIFICATION.md` | 2026-03-13 | Confirmed runtime lifecycle risk; lifecycle hardening done (IPanelLifecycle, IDisposable, no ContinueWith) |
| **Retained-Async Baseline** | `.ci/retained_async_baseline.txt` | 2026-03-13 | Known violations; check fails only on NEW violations (Option C) |
| **Retained-Async Exemptions** | `docs/design/RETAINED_ASYNC_EXEMPTIONS.md` | 2026-03-15 | Baseline strategy, exemption rationale, documented exemptions |
| **Retained-Async Risk Assessment** | `docs/design/RETAINED_ASYNC_RISK_ASSESSMENT.md` | 2026-03-15 | Top 5 high-risk ViewModels; staleness guard remediation path |
| **Golden Path Proof Status** | `docs/reports/verification/GOLDEN_PATH_PROOF_STATUS.md` | 2026-03-15 | Golden path E2E proof requirements, blocker (STT), verification steps |
| **Release XAML Smoke Gate** | `docs/design/RELEASE_XAML_SMOKE_GATE.md` | 2026-03-15 | Where Release XAML smoke runs; manual, not CI gate |
| **Playback Entry Points** | `docs/design/PLAYBACK_ENTRY_POINTS.md` | 2026-03-16 | Global transport UX; playback entry points map; panel play affordances; transport ownership rules |
| **Transport Panel Publishers** | `docs/design/TRANSPORT_PANEL_PUBLISHERS.md` | 2026-03-16 | Audit of when each panel sets/clears transport ownership; last-writer-wins rules |
| **Hardening Wave Closure** | `docs/reports/verification/HARDENING_WAVE_CLOSURE_2026.md` | 2026-03-16 | Release-Trust Hardening Wave closure; testhost teardown, Library lifecycle, playback, proof integrity |
| **Release Trust Closure (full verify lane)** | `docs/reports/release_trust_closure_20260320.md` | 2026-03-20 | Two green full `verify.ps1` runs; stub synthesis; Backend Integration subprocess + curated pytest; UI smoke; architecture wave unblocked |
| **Transport Wave 4 Shell Decomposition** | `docs/design/TRANSPORT_WAVE_4_SHELL_DECOMPOSITION_PLAN.md` | 2026-03-16 | Transport shortcut coordinator, import workflow extraction, PlayableMediaContext, smoke automation |
| **Startup Orchestration Hardening** | `docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md` | 2026-03-16 | Backend auto-start hardening: explicit phases, startup states, production runtime discovery, readiness gate, failure recovery UX |
| **Backend Ownership Policy** | `docs/design/BACKEND_OWNERSHIP_POLICY.md` | 2026-03-14 | Backend lifecycle rules: reuse, port conflict, stale backend, frontend exit, app root, runtime discovery |
| **BackendClient transport extraction inventory** | `docs/design/BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md` | 2026-03-22 | PR-1–PR-12 extraction log; Post-PR-12 remainder pointer; stop criteria link |
| **BackendClient remainder inventory** | `docs/design/BACKENDCLIENT_REMAINDER_INVENTORY.md` | 2026-03-24 | Post-PR-17 re-baseline from code; stop criteria; decision PAUSE; re-entry rule; date integrity repaired |
| **STATE archived history** | `docs/archive/STATE_HISTORY.md` | 2026-02-24 | Archived from .cursor/STATE.md per STATE_TRIM_PLAN (Post-PR-17 remainder reassessment) |
| **Extraction stop criteria** | `docs/design/EXTRACTION_STOP_CRITERIA.md` | 2026-03-22 | When NOT to extract; leverage threshold, fragmentation cost, sparse callers, DTO glue, cross-cutting |
| **STATE trim plan** | `docs/design/STATE_TRIM_PLAN.md` | 2026-03-22 | Bounded plan for STATE.md archive; do not execute without approval |
| **PR-13 Pipeline scope** | `docs/design/PR-13_PIPELINE_SCOPE.md` | 2026-03-22 | Frozen PR-13 slice: GetPipelineProvidersAsync, ProcessPipelineAsync to IPipelineConversationClient |
| **PR-14 BackupRestore scope** | `docs/design/PR-14_BACKUP_RESTORE_SCOPE.md` | 2026-03-22 | Frozen PR-14 slice: 7 backup methods to IBackupRestoreClient |
| **PR-15 Models scope** | `docs/design/PR-15_MODELS_SCOPE.md` | 2026-03-23 | Frozen PR-15 slice: 9 model methods to IModelManagerClient / pipeline |
| **PR-16 Video scope** | `docs/design/PR-16_VIDEO_SCOPE.md` | 2026-03-23 | Frozen PR-16 slice: 5 video methods to IVideoGenClient, IVideoEditClient / pipeline |
| **PR-17 Mixer scope** | `docs/design/PR-17_MIXER_SCOPE.md` | 2026-03-23 | Frozen PR-17 slice: 19 mixer methods to IMixerStateClient / pipeline; 4 gap methods added to interface |
| **Post-extraction transition plan** | `docs/design/POST_EXTRACTION_TRANSITION_PLAN.md` | 2026-03-24 | Next active lane after extraction pause; re-entry triggers; proof expectations |
| **Cross-feature workflow backlog** | `docs/design/CROSS_FEATURE_WORKFLOW_BACKLOG.md` | 2026-03-26 | **Workflow 6** Pass 06 slices **1–5** + global Quick **`20260326_145710`**. **Workflow 8** — **W8-C1/2/3** closed (historical closure Quick ≠ global unless pointer); hermetic **`8ba6363f`**. |
| **Workflow Pass 01 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_01_PROFILE_SYNTHESIS_TIMELINE.md` | 2026-03-24 | Bounded pass: Profile → synthesis → timeline |
| **Workflow Pass 02 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_02_PROJECT_TIMELINE_EFFECTS_MIXER.md` | 2026-03-24 | Bounded pass: Project → timeline → effects/mixer; closure §12–§14 |
| **Workflow Pass 02 proof reconciliation** | `docs/design/WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md` | 2026-03-24 | Incomplete verify runs vs latest_pointer; Pass 02 authoritative artifact |
| **Workflow Pass 03 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_03_SEARCH_PANEL_FOCUS_NAVIGATION.md` | 2026-03-24 | **Complete** (2026-03-24): Search → panel focus → item navigation; proof `artifacts/verify/20260324_030133` |
| **Workflow Pass 03 governance reconciliation** | `docs/design/WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md` | 2026-03-24 | Mapper path canon (`VoiceStudio.Core/Panels`); pointer rule; audit checklist vs split-brain |
| **Workflow Pass 04 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md` | 2026-03-24 | **Complete** (2026-03-24): Script editor → synthesis / preview; proof `artifacts/verify/20260324_070722`; §10 implementation lock; C5 deferred |
| **Workflow Pass 05 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md` | 2026-03-24 | **Slices 1–3 complete** — C3 Option B (proof `20260324_190103`). Slices 1–2 proofs `20260324_173141`, `20260324_181021`. **§10.7** lock; matrix **C3-OptB**. Code-truth §1 (`TranscribeViewModel`, not `TranscriptionViewModel`) |
| **Workflow Pass 06 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_06_BACKUP_RESTORE_PROJECT_SETTINGS_PROFILE_RECOVERY.md` | 2026-03-26 | **Pass 06 open; slices 1–5 complete** (§8). **Slice 5** D6 + global Quick **`20260326_145710`** / verified commit **`e2819074`**; seam **32** + pytest **8** (§7.2). **Slice 4** Quick **`20260325_055851`** (historical row). |
| **Workflow Pass 07 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md` | 2026-03-25 | **W7-C1 closed**; **lane paused after W7-C1** (**§8.4**) — Quick **`20260325_162114`**, seam **2**; [backlog](../design/CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 7 |
| **Workflow Pass 08 scope** | `docs/design/WORKFLOW_COHERENCE_PASS_08_QUALITY_BENCHMARK_PROFILE_COMPARISON.md` | 2026-03-26 | **W8-C1** + **W8-C2** + **W8-C3** closed. **§8.3** / **§8.10** / **§8.7** proofs; **§8.8–§8.9** = W8-C2 freeze + sign-off. Hermetic baseline **`20260326_020644`** / **`8ba6363f`**. [backlog](../design/CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 8 |
| **Product trust / release honesty Pass 01** | `docs/design/PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md` | 2026-03-26 | **Paused after slice 4** (§8.9 **Option 1**); slices **1–4** closed; **no slice 5** authorized; reopen = new §8 sign-off; slice 4 Quick **`20260325_143041`** / seam **5** |
| **Pass 05 C3 persistence policy** | `docs/design/PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md` | 2026-03-24 | **Option B frozen** 2026-03-24 — decisions §2, OUT §5, matrix **C3-OptB**, pre-map §8 |
| **Pass 05 persistence Option C follow-up** | `docs/design/WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md` | 2026-03-25 | Record-only `IProjectAudioClient` bridge; §8 execution |
| **Pass 05 persistence follow-up (Option A — transcribe + import)** | `docs/design/WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_A_FOLLOWUP.md` | 2026-03-27 | **A1–A4 complete.** A4: Quick **`20260326_163358`**, seam **54** (§7); **`LibraryDragDropToProjectPersistence`** + `LibraryView` drag-drop; commit **`3ad39e35`**. |
| **Stash T2 verify/CI execution row** | `docs/design/STASH0_T2_VERIFY_CI_EXECUTION_ROW.md` | 2026-03-26 | **`GOV-STASH0-T2-VERIFY-01`** — **closed**; six-path lock landed; Quick **`20260326_211554`** / **`dc07e515`**; §8 proof in doc; **`stash@{0}`** retained (selective checkout). |
| **Stash T1-S1 synthesis vertical execution row** | `docs/design/STASH0_T1_S1_EXECUTION_ROW.md` | 2026-03-27 | **`GOV-STASH0-T1-S1-01`** — **closed** on **`main`**; merge/integration verify **`git branch --contains f6047797 main`**; eight-path **§4**; Quick **`20260326_230934`** / pointer **`f6047797`**; **`run_verification`** **`20260327-151108`**; §8; **`stash@{0}`** **parked**. |
| **Stash T1 backend preflight (original decomposition)** | `docs/design/STASH0_T1_PREFLIGHT_EXECUTION_ROW.md` | 2026-03-27 | **`GOV-STASH0-T1-PREFLIGHT-01`** — **T1-C1–C4** clusters; **T1-S1** consumed **T1-C1**; **§3** baseline updated post-closure; remainder → **R1** row. |
| **Stash T1 remainder preflight (post–T1-S1)** | `docs/design/STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md` | 2026-03-27 | **`GOV-STASH0-T1-R1-PREFLIGHT-01`** — **`stash@{0}`** vs **`main`** re-baseline; **§5 Option A** slice choice **2026-03-27**; **planning-only** (no **§4** lock); next → **`GOV-STASH0-T1-R1A-EXEC-01`** ([`STASH0_T1_R1A_EXECUTION_ROW.md`](design/STASH0_T1_R1A_EXECUTION_ROW.md)). |
| **Stash T1-R1A narrow T1-C2 execution row** | `docs/design/STASH0_T1_R1A_EXECUTION_ROW.md` | 2026-03-27 | **`GOV-STASH0-T1-R1A-EXEC-01`** — **ten-path** **§4** (ancillary routes + registry + helpers); **§1** implementation **Pending**; **§5** incl. regression **T1-S1** pytest pair; **`stash@{0}`** selective extract **§7**. |
| **PR-8 Telemetry/Diagnostics scope** | `docs/design/PR-8_TELEMETRY_DIAGNOSTICS_SCOPE.md` | 2026-03-22 | PR-8 extraction scope: Option A (DiagnosticsClient decoupling) or Option B (Macros) |
| **Premium Software Coherence Audit** | `docs/design/PREMIUM_SOFTWARE_COHERENCE_AUDIT.md` | 2026-03-17 | Formal audit: startup, shell, transport, panel lifecycle, event wiring, backend seam, workflows, UX; gaps ranked S0–S2 |

## Architecture

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Architecture (Comprehensive)** | `docs/developer/ARCHITECTURE.md` | 2026-02-04 | **CANONICAL** — Complete architecture reference (2400+ lines); supersedes all docs/design/architecture*.md files (archived to `docs/archive/architecture_consolidated/`) |
| Architecture Index | `docs/architecture/README.md` | 2026-01-25 | Entry point; architecture content lives in README + ADRs. |
| System Architecture (Part series) | `docs/architecture/Part*.md` | — | **DEFERRED** — 10-part series from ChatGPT spec; use `docs/architecture/README.md` + ADRs as canonical architecture source |
| Decisions (ADRs) | `docs/architecture/decisions/ADR-*.md` | 2026-01-25 | Architecture Decision Records |
| ADR Index | `docs/architecture/decisions/README.md` | 2026-01-25 | ADR listing and template |
| Rulebook Integration ADR | `docs/architecture/decisions/ADR-001-rulebook-integration.md` | 2026-01-25 | Rulebook and rule governance |
| Document Governance ADR | `docs/architecture/decisions/ADR-002-document-governance.md` | 2026-01-25 | Document governance and lifecycle |
| Agent Governance Framework ADR | `docs/architecture/decisions/ADR-003-agent-governance-framework.md` | 2026-01-25 | Agent governance and roles |
| MessagePack IPC ADR | `docs/architecture/decisions/ADR-004-messagepack-ipc.md` | 2026-01-25 | MessagePack for IPC serialization |
| Context Management ADR | `docs/architecture/decisions/ADR-005-context-management.md` | 2026-01-25 | Context management system |
| Cursor Rules ADR | `docs/architecture/decisions/ADR-006-cursor-rules-system.md` | 2026-01-25 | Enhanced Cursor rules system |
| IPC Boundary ADR | `docs/architecture/decisions/ADR-007-ipc-boundary.md` | 2026-01-25 | UI-Backend IPC boundary definition |
| Architecture Patterns ADR | `docs/architecture/decisions/ADR-008-architecture-patterns.md` | 2026-01-25 | Core architecture patterns |
| AI-Native Development ADR | `docs/architecture/decisions/ADR-009-ai-native-development.md` | 2026-01-25 | AI-native development patterns |
| Native Windows Platform ADR | `docs/architecture/decisions/ADR-010-native-windows-platform.md` | 2026-01-25 | WinUI 3 native platform choice |
| Context Manager Architecture ADR | `docs/architecture/decisions/ADR-011-context-manager-architecture.md` | 2026-01-25 | Context manager architecture |
| Roadmap Integration ADR | `docs/architecture/decisions/ADR-012-roadmap-integration.md` | 2026-01-25 | Roadmap integration scaffolding |
| OpenTelemetry Tracing ADR | `docs/architecture/decisions/ADR-013-opentelemetry-tracing.md` | 2026-01-25 | OpenTelemetry distributed tracing |
| Agent Skills ADR | `docs/architecture/decisions/ADR-014-agent-skills.md` | 2026-01-25 | Agent skills integration |
| Architecture Integration Contract ADR | `docs/architecture/decisions/ADR-015-architecture-integration-contract.md` | 2026-01-25 | Integration contract |
| **Gate C Artifact Choice ADR** | `docs/architecture/decisions/ADR-016-gate-c-artifact-choice.md` | 2026-01-29 | Unpackaged self-contained apphost EXE as Gate C launch artifact |
| Engine Subprocess Model ADR | `docs/architecture/decisions/ADR-017-engine-subprocess-model.md` | 2026-01-25 | Engine subprocess isolation model |
| Named Pipes to HTTP ADR | `docs/architecture/decisions/ADR-018-named-pipes-http.md` | 2026-01-25 | Named pipes replaced with HTTP |
| Orchestration in Python ADR | `docs/architecture/decisions/ADR-019-orchestration-in-python.md` | 2026-01-25 | C# orchestration moved to Python |
| UI Assembly Split ADR | `docs/architecture/decisions/ADR-023-ui-assembly-split.md` | 2026-01-30 | UI assembly modularization |
| Completion Evidence Guard ADR | `docs/architecture/decisions/ADR-024-completion-evidence-guard.md` | 2026-02-01 | Enforce completion markers committed before verification passes |
| **Compatibility Matrix ADR** | `docs/architecture/decisions/ADR-025-compatibility-matrix-and-scaffolding.md` | 2026-02-02 | Centralized version pins, scaffolding tools, CODEOWNERS, AI agent safety |
| **Infrastructure Remediation ADR** | `docs/architecture/decisions/ADR-026-infrastructure-remediation.md` | 2026-02-02 | Activation of dormant development infrastructure (telemetry, issues, context) |
| **Unified Verification Harness ADR** | `docs/architecture/decisions/ADR-027-unified-verification-harness.md` | 2026-02-09 | Single command verification, 8 stages, fail-fast, "no green = no merge" rule |
| **Unified Command Architecture ADR** | `docs/architecture/decisions/ADR-028-unified-command-architecture.md` | 2026-02-08 | Hybrid command system: Registry for global/routed, ViewModel for panel-local |
| Hybrid Supervisor ADR | `docs/architecture/decisions/ADR-029-hybrid-supervisor.md` | 2026-02-09 | Hybrid supervisor architecture |
| **ViewModel DI Migration ADR** | `docs/architecture/decisions/ADR-030-viewmodel-di-migration.md` | 2026-02-09 | ViewModel constructor DI migration from service locator |
| API Versioning Strategy ADR | `docs/architecture/decisions/ADR-031-api-versioning-strategy.md` | 2026-02-10 | API versioning and evolution strategy |
| Middleware Stack ADR | `docs/architecture/decisions/ADR-032-middleware-stack.md` | 2026-02-10 | FastAPI middleware ordering and architecture |
| Config Consolidation ADR | `docs/architecture/decisions/ADR-033-config-consolidation.md` | 2026-02-10 | Configuration management consolidation |
| Enhanced Engine Routing ADR | `docs/architecture/decisions/ADR-034-enhanced-engine-routing.md` | 2026-02-11 | Enhanced engine routing and selection |
| **Sentinel Deterministic Workflow ADR** | `docs/architecture/decisions/ADR-035-sentinel-deterministic-workflow.md` | 2026-02-12 | 7-step sentinel workflow for reproducible pipeline validation |
| **Plugin System Unification ADR** | `docs/architecture/decisions/ADR-036-plugin-system-unification.md` | 2026-02-16 | Unified plugin architecture: single manifest schema, bridge service, permission model, phased implementation |
| **Plugin Trust Lane Model ADR** | `docs/architecture/decisions/ADR-037-plugin-trust-lane-model.md` | 2026-02-16 | Lane A (trusted in-process) for Phase 3; Lane B (isolated) deferred to Phase 4+ |
| **Plugin ABC Unification ADR** | `docs/architecture/decisions/ADR-038-plugin-abc-unification.md` | 2026-02-17 | Unified Plugin ABC with mixins; deprecates BasePlugin and PluginBase |
| **Phase 6 Strategic Maturity ADR** | `docs/architecture/decisions/ADR-039-phase6-strategic-maturity.md` | 2026-02-18 | Wasm runtime (wasmtime), AI quality, compliance, ecosystem, PQC research |
| **Dual Plugin Loader ADR** | `docs/architecture/decisions/ADR-040-dual-plugin-loader.md` | 2026-02-18 | Documents dual loader architecture (PluginLoader vs PluginService) and usage guidelines |
| **Model Lifecycle Strategy ADR** | `docs/architecture/decisions/ADR-043-model-lifecycle-strategy.md` | 2026-02-21 | Model registry, baselines, rollback, A/B testing integration |
| **Supply-Chain Integrity ADR** | `docs/architecture/decisions/ADR-044-supply-chain-integrity.md` | 2026-02-21 | Full-app SBOM, dependency hashes, installer provenance (Phase 12 WS2) |
| **Intelligent Engine Orchestrator ADR** | `docs/architecture/decisions/ADR-045-orchestrator-architecture.md` | 2026-02-25 | OrchestrationService, quality-driven retry, backend/orchestrator/ |
| **Delete Mediator/CQRS Layer ADR** | `docs/architecture/decisions/ADR-046-delete-mediator-cqrs-layer.md` | 2026-03-03 | backend/application/ removed; routes call services directly |
| **WinUI 3 XamlRoot Deferral ADR** | `docs/architecture/decisions/ADR-047-winui-xamlroot-deferral-pattern.md` | 2026-03-10 | Defer panel/overlay init to Loaded; never fire-and-forget XamlRoot-using async from Window constructor |
| **Centralized Request Coordination ADR** | `docs/architecture/decisions/ADR-048-centralized-request-coordination.md` | 2026-03-11 | IRequestCoordinator for single-flight, TTL, invalidation; BackendClient delegates; ProfilesViewModel simplified |
| **MCP Integration Strategy ADR** | `docs/architecture/decisions/ADR-049-mcp-integration-strategy.md` | 2026-02-21 | MCP POC status, planned capabilities, roadmap (Arch Review 1.6) |

## Planning and Roadmaps

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Ultimate Master Plan 2026 (Optimized)** | `docs/governance/ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md` | 2026-02-04 | **ACTIVE PLAN** — 8 phases, 145 tasks, optimized role assignments. Supersedes prior plan versions. |
| **Unified Master Roadmap** | `docs/governance/MASTER_ROADMAP_UNIFIED.md` | 2026-01-25 | **Primary canonical roadmap** - consolidates all previous roadmaps |
| **Optional Task Inventory** | `docs/governance/OPTIONAL_TASK_INVENTORY.md` | 2026-01-29 | Authoritative optional-task backlog and dependency map; Phase 1 Master Plan deliverable |
| Master Roadmap (Legacy) | `docs/archive/governance/MASTER_ROADMAP.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md. Note: `docs/archive/governance/` may be missing; create and move legacy roadmaps if archive policy requires. See [Final Sweep (Pre-Realignment)](../reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md) §2, §6.1. |
| Roadmap Summary (Legacy) | `docs/archive/governance/MASTER_ROADMAP_SUMMARY.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md |
| Roadmap Index (Legacy) | `docs/archive/governance/MASTER_ROADMAP_INDEX.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md |
| Task Tracking | `docs/governance/MASTER_TASK_CHECKLIST.md` | 2026-01-25 | **ARCHIVED** to `docs/archive/governance/` (GAP-DOC-003). Use `docs/tasks/` for active task briefs. |
| Task Log | `docs/governance/TASK_LOG.md` | 2026-01-25 | Historical task log |
| Phase Gates | `docs/governance/PHASE_GATES_EVIDENCE_MAP.md` | 2026-01-25 | Gate completion evidence |
| Risk Register | `docs/governance/RISK_REGISTER.md` | 2026-01-25 | Known risks and mitigations |
| Service Level Objectives | `docs/governance/SERVICE_LEVEL_OBJECTIVES.md` | 2026-01-25 | SLOs and telemetry-to-backlog integration |
| Architecture Integration Phase 4 Backlog | `docs/design/ARCHITECTURE_INTEGRATION_BACKLOG.md` | 2026-01-28 | R10/R11 done; R12 (skills-as-MCP) backlog |
| **Plugin Phase 3 Remediation Plan** | `docs/design/PLUGIN_PHASE3_REMEDIATION_PLAN.md` | 2026-02-16 | Findings and sprint plan from Phase 3 architectural review |
| **Timeline Hardening and Next Tasks Plan** | `docs/design/TIMELINE_HARDENING_AND_NEXT_TASKS_PLAN_2026-03-11.md` | 2026-03-11 | Ruthless assessment; verification of 9.4/11.1; Task 10.1 partial status; next 8 tasks in order (Timeline 1A–1C, dialog baseline, audit, proof, mypy, workflow, skip-debt) |
| **Next 10 Tasks Plan v2** | `docs/design/NEXT_10_TASKS_PLAN_V2.md` | 2026-03-13 | Seam migration (MultiVoiceGenerator, EnsembleSynthesis, MiniTimeline, Automation, GlobalSearch, EffectsMixer); lifecycle audit; seam test audit; doc fix; verification gate |
| **Comprehensive Gap Analysis and Remediation Plan** | `docs/design/COMPREHENSIVE_GAP_ANALYSIS_AND_REMEDIATION_PLAN.md` | 2026-03-11 | Verified work completed vs. remaining; 9 gaps logged; 8-phase remediation plan (R1–R8); P1–P4 priorities |
| **MainWindow Decomposition Plan** | `docs/design/MAINWINDOW_DECOMPOSITION_PLAN.md` | 2026-03-15 | Next extraction target: Status Bar Orchestration; future slices: import flow, navigation-shell |
| **Search Overlay Ownership Contract** | `docs/design/SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md` | 2026-03-21 | Coordinator vs shell responsibilities; prevents search coordinator blob |
| **Workflow Toast Wiring Proof** | `docs/design/WORKFLOW_TOAST_WIRING_PROOF.md` | 2026-03-19 | Evidence chain for IToastNotificationService production wiring; interface-compatible end-to-end |
| **Cross-Role Escalation Matrix** | `docs/governance/CROSS_ROLE_ESCALATION_MATRIX.md` | 2026-01-29 | Decision tree and routing table for cross-role escalation; when to use Debug Agent vs other roles |
| **Handoff Protocol** | `docs/governance/HANDOFF_PROTOCOL.md` | 2026-01-29 | Standardized protocol for issue escalation and cross-role handoffs; templates and examples |

## References

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| API Reference | `docs/REFERENCE/` | 2026-01-25 | Consolidated API docs |
| Engine Reference | `docs/REFERENCE/ENGINE_REFERENCE.md` | 2026-01-25 | Engine capabilities and config |
| Engine Config | `backend/config/engine_config.json` | 2026-01-25 | Runtime engine configuration |
| **AutomationId Registry** | `docs/developer/AUTOMATION_ID_REGISTRY.md` | 2026-02-09 | Authoritative registry of stable AutomationIds; treat as public API; naming conventions and deprecation process |
| **Connector/working-tree alignment** | `docs/developer/CONNECTOR_WORKING_TREE_ALIGNMENT.md` | 2026-03-22 | Expected root, branch, verify connector freshness; detect stale STATE.md |
| Overseer Reference | `docs/REFERENCE/OVERSEER_REFERENCE.md` | 2026-01-25 | Overseer tooling guide |
| Workers Reference | `docs/REFERENCE/WORKERS_REFERENCE.md` | 2026-01-25 | Worker system documentation |
| Project Status | `docs/REFERENCE/PROJECT_STATUS_REFERENCE.md` | 2026-01-25 | Current project status |
| Comprehensive Issues | `docs/REFERENCE/COMPREHENSIVE_ISSUES_REFERENCE.md` | 2026-01-25 | Known issues tracker |
| Storage Durability | `docs/REFERENCE/STORAGE_DURABILITY_REFERENCE.md` | 2026-01-27 | Atomic-write audit and reference (Role 4) |
| Job Runtime Map | `docs/REFERENCE/JOB_RUNTIME_MAP_REFERENCE.md` | 2026-01-27 | Job flows, cancellation, JobStateStore (Role 4) |
| Preflight | `docs/REFERENCE/PREFLIGHT_REFERENCE.md` | 2026-01-27 | Port 8001, intended use, plugin-dir (Role 4) |
| Artifact & Model | `docs/REFERENCE/ARTIFACT_MODEL_REFERENCE.md` | 2026-01-27 | Artifact/model storage, durability, preflight (Role 4) |

## Role Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Role Guides Index** | `docs/governance/ROLE_GUIDES_INDEX.md` | 2026-01-30 | Master index with phase-gate-role matrix, role ownership by module, invocation commands |
| Role 0: Overseer | `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md` | 2026-01-25 | Gate enforcement, evidence, coordination |
| Role 1: System Architect | `docs/governance/roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md` | 2026-01-25 | Boundaries, contracts, ADRs |
| Role 2: Build & Tooling | `docs/governance/roles/ROLE_2_BUILD_TOOLING_GUIDE.md` | 2026-01-25 | Deterministic builds, CI/CD |
| Role 3: UI Engineer | `docs/governance/roles/ROLE_3_UI_ENGINEER_GUIDE.md` | 2026-01-25 | MVVM, VSQ tokens, WinUI 3 |
| Role 4: Core Platform | `docs/governance/roles/ROLE_4_CORE_PLATFORM_GUIDE.md` | 2026-01-25 | Runtime, storage, preflight |
| Role 5: Engine Engineer | `docs/governance/roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md` | 2026-01-25 | Quality metrics, adapters |
| Role 6: Release Engineer | `docs/governance/roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md` | 2026-01-25 | Installer, lifecycle, Gate H |
| **Role 7: Debug Agent** | `docs/governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md` | 2026-01-25 | Root-cause analysis, issue triage, system-wide fixes, validation |
| Skeptical Validator (subagent) | `docs/governance/SKEPTICAL_VALIDATOR_GUIDE.md` | 2026-01-28 | Cross-cutting validation subagent; §7 "When to Use" |
| Validator Escalation Protocol | `docs/governance/VALIDATOR_ESCALATION.md` | 2026-01-28 | Overseer queue, HIGH PRIORITY, escalation triggers |
| **Overseer Final Handoff** | `docs/governance/overseer/handoffs/OVERSEER_FINAL_HANDOFF.md` | 2026-02-18 | Successor handoff: architecture, risks, file map, verification playbook, recommendations (10 sections) |
| **Overseer Session Handoff 2026-03-13** | `docs/governance/overseer/handoffs/OVERSEER_SESSION_HANDOFF_2026-03-13.md` | 2026-03-13 | Session handoff: ImageSearch migration complete; completion_guard FAIL; next target Rank 7 (TemplateLibraryViewModel) |
| **Overseer Session Handoff 2026-03-14** | `docs/governance/overseer/handoffs/OVERSEER_SESSION_HANDOFF_2026-03-14.md` | 2026-03-14 | Session handoff: VoiceMorphingBlendingViewModel migration; VoiceBrowserViewModelTests fix; 61 migrated; regenerate queue for next target |
| **Overseer Session Handoff 2026-03-16** | `docs/governance/overseer/handoffs/OVERSEER_SESSION_HANDOFF_2026-03-16.md` | 2026-03-16 | Session handoff: Startup Orchestration Round 2 plan truth sync; Transport Wave 4 complete; commit plan docs if completion_guard FAILs |
| Context Manager Integration | `docs/governance/CONTEXT_MANAGER_INTEGRATION.md` | 2026-01-25 | Context manager architecture, ownership, and usage by role |
| Role Boundaries Protocol | `Recovery Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md` | 2026-01-25 | Role playbooks, handshake rules |
| Role Cheatsheet | `docs/developer/ROLE_CHEATSHEET.md` | 2026-01-25 | Quick one-liner prompts |

## Role System Prompts

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Role Prompts Index** | `.cursor/prompts/ROLE_PROMPTS_INDEX.md` | 2026-01-25 | Master index for all 7 role prompts |
| Role 0: Overseer Prompt | `.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Overseer |
| Role 1: System Architect Prompt | `.cursor/prompts/ROLE_1_SYSTEM_ARCHITECT_PROMPT.md` | 2026-01-25 | Complete system prompt for System Architect |
| Role 2: Build & Tooling Prompt | `.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md` | 2026-01-25 | Complete system prompt for Build & Tooling |
| Role 3: UI Engineer Prompt | `.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for UI Engineer |
| Role 4: Core Platform Prompt | `.cursor/prompts/ROLE_4_CORE_PLATFORM_PROMPT.md` | 2026-01-25 | Complete system prompt for Core Platform |
| Role 5: Engine Engineer Prompt | `.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Engine Engineer |
| Role 6: Release Engineer Prompt | `.cursor/prompts/ROLE_6_RELEASE_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Release Engineer |
| **Role 7: Debug Agent Prompt** | `.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md` | 2026-01-25 | Complete system prompt for Debug Agent |
| Skeptical Validator Prompt | `.cursor/prompts/SKEPTICAL_VALIDATOR_PROMPT.md` | 2026-01-28 | Kickoff prompt for Skeptical Validator subagent (v1.1.0: role identity fix, validator_workflow.py integration, Quality Ledger clarification) |
| Onboarding Summary | `.cursor/prompts/ONBOARDING_COMPLETE_SUMMARY.md` | 2026-01-25 | Overseer onboarding completion report |

## Agent Skills

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Agent Skills | `.cursor/skills/` | 2026-01-28 | Role and tool skills for Cursor Agent |
| Skill Registration Script | `tools/skills/register_skill.ps1` | 2026-01-28 | Scaffold for new skills and templates |

## Developer Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Quick Start | `docs/governance/QUICK_START_GUIDE.md` | 2026-01-25 | Getting started for devs |
| README First | `docs/governance/README_FIRST.md` | 2026-01-25 | First-time contributor guide |
| Developer Guide | `docs/developer/DEVELOPER_GUIDE.md` | 2026-01-25 | Development practices |
| Build & Deploy | `docs/developer/BUILD_AND_DEPLOYMENT.md` | 2026-01-25 | Build process |
| Contributing | `docs/developer/CONTRIBUTING.md` | 2026-01-25 | Contribution guidelines |
| Onboarding | `docs/developer/ONBOARDING.md` | 2026-01-25 | New developer onboarding |
| Troubleshooting | `docs/developer/TROUBLESHOOTING.md` | 2026-01-25 | Common issues and fixes |
| Cursor User Rules | `docs/developer/CURSOR_USER_RULES.md` | 2026-01-25 | Global Cursor baseline for VoiceStudio |
| **Compatibility Matrix Guide** | `docs/developer/COMPATIBILITY_MATRIX_GUIDE.md` | 2026-02-02 | How to use and update the compatibility matrix; validation workflow |
| **AI Agent Development Guide** | `docs/developer/AI_AGENT_DEVELOPMENT_GUIDE.md` | 2026-02-02 | AI-assisted development best practices; scaffold usage, matrix checks |
| **Scaffolding Tools** | `tools/scaffolds/` | 2026-02-02 | CLI scaffolds: `generate_panel.py`, `generate_route.py`, `generate_engine.py` |
| **XAML Change Protocol** | `docs/developer/XAML_CHANGE_PROTOCOL.md` | 2026-02-04 | Mandatory procedures for XAML changes; forbidden patterns, binlog analysis workflow, Views subfolder protection |
| **UI Hardening Guidelines** | `docs/developer/UI_HARDENING_GUIDELINES.md` | 2026-02-04 | XAML stability best practices; UserControl extraction, ResourceDictionary organization, binding anti-patterns |
| **Panel Hardening Pattern** | `docs/developer/PANEL_HARDENING_PATTERN.md` | 2026-03-11 | Request coordination, dialog handling, selection-change cancellation, cache invalidation; Profiles reference implementation |
| **Phase 6 Developer Guide** | `docs/developer/PHASE6_DEVELOPER_GUIDE.md` | 2026-02-18 | Wasm plugins, AI quality, compliance, ecosystem, incubator features |
| **Plugin Privacy Guide** | `docs/developer/PLUGIN_PRIVACY_GUIDE.md` | 2026-02-18 | GDPR-inspired privacy framework; data categories, consent management, user rights |
| **Error Handling Guide** | `docs/developer/ERROR_HANDLING_GUIDE.md` | 2026-03-10 | Unified error envelope, error codes, severity levels, propagation patterns; schema: `shared/schemas/error-envelope.schema.json` (GAP-010 complete) |
| **WebSocket Guide** | `docs/developer/WEBSOCKET_GUIDE.md` | 2026-03-06 | WebSocket architecture, topics, message format, connection management; see `docs/REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md` for topic reference (GAP-013 complete) |
| **UI Virtualization Guide** | `docs/developer/UI_VIRTUALIZATION_GUIDE.md` | 2026-02-04 | List virtualization patterns, incremental loading, performance guidelines (GAP-014) |
| **Command Palette Guide** | `docs/developer/COMMAND_PALETTE_GUIDE.md` | 2026-03-10 | IUnifiedCommandRegistry, CommandPaletteService, Ctrl+P, action types; GAP-015 complete |
| **Schema Sync Workflow** | `docs/developer/SCHEMA_SYNC.md` | 2026-02-11 | Schema ownership, validation, and synchronization workflow; shared/schemas/ governance |
| **Workspace manual test steps** | `docs/testing/WORKSPACE_MANUAL_TEST_STEPS.md` | 2026-02-12 | Manual verification steps for workspace dropdown and profile switching; optional Gate C note |
| **Sentinel Testing Guide** | `docs/developer/SENTINEL_TESTING_GUIDE.md` | 2026-02-12 | Sentinel workflow usage, configuration, test writing, debugging with repro packets |
| **UI Automation Guide** | `docs/developer/UI_AUTOMATION_GUIDE.md` | 2026-02-13 | **NEW** — WinAppDriver + Page Object Model, AutomationId standards, smoke tests, CI integration |
| **CI Suppression Policy** | `docs/developer/CI_SUPPRESSION_POLICY.md` | 2026-03-11 | When CI steps may use non-blocking patterns (e.g. continue-on-error); advisory vs gate distinction; inventory |
| **OpenAPI CI Alignment** | `docs/developer/OPENAPI_CI_ALIGNMENT.md` | 2026-03-11 | Dep alignment for OpenAPI export/validation; scripts that import backend.api.main must use requirements.txt |
| **Architecture Foundations Guide** | `docs/developer/ARCHITECTURE_FOUNDATIONS_GUIDE.md` | 2026-02-14 | DI system, API versioning, caching layer, message queue, database migrations |
| **Scalability & Resilience Guide** | `docs/developer/SCALABILITY_RESILIENCE_GUIDE.md` | 2026-02-14 | Circuit breakers, rate limiting, retry logic, timeout config, horizontal scaling |
| **Production Readiness Guide** | `docs/operations/PRODUCTION_READINESS_GUIDE.md` | 2026-02-14 | Installer system, crash recovery, error handling, performance optimization, deployment checklist |
| **API Contract Tests** | `tests/integration/test_api_contracts.py` | 2026-02-14 | JSON Schema validation for API contracts against sentinel schemas |
| **Continuous Improvement Guide** | `docs/developer/CONTINUOUS_IMPROVEMENT_GUIDE.md` | 2026-02-14 | **NEW** — Feature flags, feedback collection, quality automation, documentation as code (Phase 8) |

## Reference Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Sentinel Contract Schemas** | `docs/REFERENCE/SENTINEL_CONTRACT_SCHEMAS.md` | 2026-02-12 | JSON Schema contracts for sentinel workflow; versioning policy, validation examples |
| **WebSocket Topics** | `docs/REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md` | 2026-03-06 | Canonical topic reference for /ws/realtime; payloads, broadcast APIs; GAP-013 complete |

## Build and Diagnostic Tools

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Unified Verification Harness** | `scripts/verify.ps1` | 2026-02-09 | Single source of truth for product verification; 8 stages (build, lint, tests, contracts, integration, UI, gates); "no green = no merge" |
| **Engine Adapter Contract Tests** | `tests/contract/test_engine_adapter_contracts.py` | 2026-02-09 | Protocol compliance tests for all engine adapters; method signatures, error handling, device contracts |
| **AutomationId Validator** | `scripts/validate_automation_ids.py` | 2026-02-09 | Validates AutomationId registry against XAML files; detects drift between documentation and implementation |
| **XAML Compiler Playbook** | `docs/build/XAML_COMPILER_PLAYBOOK.md` | 2026-02-04 | Single operational runbook for XAML compiler troubleshooting; decision tree, copy-paste commands, emergency recovery |
| **XAML Diagnostic Build** | `scripts/build-with-binlog.ps1` | 2026-02-04 | Reproducible single-threaded build with binlog capture for XAML debugging |
| **Binlog Analysis (PS)** | `scripts/analyze-binlog.ps1` | 2026-02-04 | PowerShell script to extract XamlCompiler invocations and detect nested Views issues; supports file output |
| **Binlog Analysis (Python)** | `scripts/analyze_binlog.py` | 2026-02-04 | Python alternative for binlog analysis; supports file output for CI integration |
| **XAML Safety Rule** | `.cursor/rules/quality/xaml-safety.mdc` | 2026-02-04 | Cursor agent safety guardrails for XAML changes; forbidden patterns, non-destructive operations |
| **C#/WinUI Rule** | `.cursor/rules/languages/csharp-winui.mdc` | 2026-02-04 | MVVM conventions, XAML binding standards, command patterns for WinUI 3 development |
| **Proactive XAML Check** | `scripts/proactive-xaml-check.ps1` | 2026-02-04 | CI health check for XAML issues: nested Views, missing x:DataType, legacy bindings |

## Design and Specifications

> **Note on docs/design/**: This folder contains 60+ files from iterative development. The canonical sources are listed below. Files not listed are either:
> - **Superseded** by `docs/developer/ARCHITECTURE.md` (canonical architecture reference)
> - **Supplementary** reference material that may be archived in future
>
> When in doubt, prefer: `docs/developer/ARCHITECTURE.md` for architecture, ADRs for decisions, and this registry for all other canonicals.

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| UI Implementation | `docs/design/UI_IMPLEMENTATION_SPEC.md` | 2026-01-25 | UI design specification |
| Implementation Spec | `docs/design/VOICESTUDIO_COMPLETE_IMPLEMENTATION_SPEC.md` | 2026-02-11 | Full implementation spec; MCP sections updated to reflect current state |
| Execution Plan (Legacy) | `docs/archive/legacy_worker_system/design/EXECUTION_PLAN.md` | 2026-01-30 | **ARCHIVED** — Legacy Overseer+8-Worker plan; use MASTER_ROADMAP_UNIFIED and PROJECT_HANDOFF_GUIDE |
| File Structure | `docs/design/file-structure.md` | 2026-01-25 | Project file organization |
| Project Structure | `docs/design/project-structure.md` | 2026-01-25 | High-level project layout |
| **ViewModel DI Refactor** | `docs/design/viewmodel_di_refactor.md` | 2026-01-30 | TD-004; migration from AppServices/parameterless BaseViewModel to constructor injection; 4-phase rollout plan |
| **Engine Venv Isolation** | `docs/design/ENGINE_VENV_ISOLATION_SPEC.md` | 2026-01-30 | TD-001; per-engine/dual-venv strategy (Chatterbox vs XTTS torch); Option C (dual venv) recommended |
| **UI Automation** | `docs/design/UI_AUTOMATION_SPEC.md` | 2026-01-30 | Hybrid Gate C + WinAppDriver; Phase 2 Master Plan mini-spec; Option D (Hybrid) decision |
| Architecture Data Flow | `docs/design/ARCHITECTURE_DATA_FLOW.md` | — | **SUPERSEDED** by `docs/developer/ARCHITECTURE.md` |
| Architecture Diagrams | `docs/design/ARCHITECTURE_DIAGRAMS.md` | — | **SUPERSEDED** by `docs/developer/ARCHITECTURE.md` |
| Implementation Status | `docs/design/IMPLEMENTATION_COMPLETE.md`, `IMPLEMENTATION_STATUS.md`, etc. | — | **SUPERSEDED** — Historical snapshots; use STATE.md and MASTER_ROADMAP_UNIFIED for current status |
| Roadmaps (design/) | `docs/design/roadmap.md`, `PHASE_2_ROADMAP.md`, etc. | — | **SUPERSEDED** by `docs/governance/ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md` |

## Project Organization

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Organization Map | `docs/governance/PROJECT_ORGANIZATION_MAP.md` | 2026-01-25 | Project structure map |
| Reorg Log | `docs/governance/PROJECT_REORG_LOG.md` | 2026-01-25 | Reorganization history |
| Compatibility Matrix (Design) | `docs/design/COMPATIBILITY_MATRIX.md` | 2026-01-30 | Human-readable compatibility matrix; see also `config/compatibility_matrix.yml` |
| Production Build | `docs/governance/VoiceStudio_Production_Build_Plan.md` | 2026-01-25 | Production build plan |
| **Deterministic Sentinel Implementation Plan** | `docs/design/DETERMINISTIC_SENTINEL_IMPLEMENTATION_PLAN.md` | 2026-02-13 | 6-phase implementation plan for sentinel workflow, API hardening, UI automation, security/stability, architecture foundations, scalability |
| **Phase F Anti-Theater Hardening Plan** | `docs/design/PHASE_F_ANTI_THEATER_HARDENING_PLAN.md` | 2026-03-04 | Sprint plan: Gate C nav_steps, stub/real proof non-fakeability, STATE canonical, god-object budgets, schema/fingerprint alignment |
| **Dialog Architecture Bulletproof Plan** | `docs/design/DIALOG_ARCHITECTURE_BULLETPROOF_PLAN.md` | 2026-03-10 | Centralize ContentDialog/XamlRoot; IProfileDialogService, IXamlRootProvider; CI guard; Profiles-first template |
| **Skip Debt Cleanup Subplan** | `docs/design/SKIP_DEBT_CLEANUP_SUBPLAN.md` | 2026-03-11 | v1.2 executable subplan; scope, sequence, burn-down 312→200, policy |
| **Workflow Consolidation Subplan** | `docs/design/WORKFLOW_CONSOLIDATION_SUBPLAN.md` | 2026-03-11 | v1.2 executable subplan; Build/CI/Tests/Sentinel duplication, blast-radius |
| **Strict Mypy Burn-Down Subplan** | `docs/design/STRICT_MYPY_BURNDOWN_SUBPLAN.md` | 2026-03-11 | v1.2 advisory subplan; routes + services, incremental strict mypy |
| **Contract Tests OpenAPI strategy** | `docs/design/CONTRACT_TESTS_OPENAPI_STRATEGY.md` | 2026-03-19 | Static vs live OpenAPI split; session fixture vs `test_openapi_contract` static file |

## Security

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Threat Model | `docs/reports/security/THREAT_MODEL.md` | 2026-01-25 | Baseline security threat model |
| **Security Configuration Guide** | `docs/operations/SECURITY_CONFIGURATION.md` | 2026-02-14 | **NEW** — Credential storage (DPAPI), error boundaries, health checks, graceful shutdown, correlation ID logging |

## Reports

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Gap Remediation Sprint Completion** | `docs/reports/audit/GAP_REMEDIATION_SPRINT_COMPLETION_2026-02-18.md` | 2026-02-18 | **NEW** — 52-task gap remediation completion report; Python lint fixes, verification harness GREEN |
| **Error Pattern Retrospective** | `docs/reports/post_mortem/ERROR_PATTERN_RETROSPECTIVE_2026-02-04.md` | 2026-02-04 | Comprehensive analysis of systemic behaviors causing recurring errors; 36 issues analyzed, role responsibility ranking, anti-pattern inventory |
| **Architecture Peer Review Package (Gate C / TASK-0004)** | `docs/reports/verification/ARCHITECTURE_PEER_REVIEW_PACKAGE_2026-01-27.md` | 2026-01-27 | Overseer-owned single entry point for architecture peer review; consolidates blockers, decisions, evidence, next tasks, approval map |
| **Complete Project Report (Start → 2026-02-02)** | `docs/reports/verification/VOICESTUDIO_COMPLETE_PROJECT_REPORT_2026-02-02.md` | 2026-02-02 | Single narrative + status + remaining gaps; links to SSOT; includes peer approval checklist |
| Session 11 Overseer Next Steps | `docs/reports/verification/SESSION11_OVERSEER_NEXT_STEPS_2026-01-27.md` | 2026-01-27 | Overseer run: tooling refresh, Task B deferred (venv), Task C partial (Install OK, Launch V1 fail); §6 peer approval |
| Peer Review Package | `docs/reports/verification/PEER_REVIEW_PACKAGE_2026-01-28.md` | 2026-01-28 | Peer review of items pending approval; tooling verification; Validator sign-off checklist |
| Gate C / Gate H Release Engineer | `docs/reports/packaging/GATE_C_H_RELEASE_ENGINEER_REPORT_2026-01-27.md` | 2026-01-27 | Gate C proof status, Gate H lifecycle plan, prereq gaps, evidence bundle |
| Rules Gap Analysis | `docs/reports/governance/RULES_GAP_ANALYSIS_REPORT.md` | 2026-01-25 | Rules Kit integration gap assessment |
| Rules Validation | `docs/reports/verification/RULES_VALIDATION_REPORT.md` | 2026-01-25 | Static validation + manual steps |
| UI Spec Reconciliation | `docs/reports/design/UI_SPEC_RECONCILIATION_MATRIX.md` | 2026-01-25 | Base vs Quantum+ comparison |
| UI Gap Analysis | `docs/reports/verification/UI_GAP_ANALYSIS_REPORT.md` | 2026-01-25 | Spec vs implementation gaps |
| **Accessibility Testing (Gate G)** | `docs/reports/verification/ACCESSIBILITY_TESTING_REPORT.md` | 2026-01-29 | Phase 4 QA; §2.1 formal screen reader procedure; Role 3 contribution |
| **Performance Testing (Phase 4)** | `docs/reports/verification/PERFORMANCE_TESTING_REPORT.md` | 2026-01-29 | Baseline UI/engine/SLO metrics; TASK-0014 Phase B |
| **Security Audit (Phase 4)** | `docs/reports/verification/SECURITY_AUDIT_REPORT.md` | 2026-01-29 | Dependency scan (pip-audit, dotnet vulnerable), code review; TASK-0014 Phase C |
| **Phase 5 Closure Report** | `docs/reports/packaging/PHASE_5_CLOSURE_REPORT_2026-01-29.md` | 2026-01-29 | Phase 5 (Packaging & Installer) formal closure; Gate H 1/1 GREEN; lifecycle 7/7 PASS; roadmap baseline complete; TASK-0017 deliverable |
| **Optional Tasks Master Plan — Stream Status** | `docs/reports/verification/ENGINE_PROOF_STREAM_STATUS_2026-01-29.md`, `CORE_PLATFORM_STREAM_STATUS_2026-01-29.md`, `UI_STREAM_STATUS_2026-01-29.md`, `BUILD_QUALITY_STREAM_STATUS_2026-01-29.md`, `OBSERVABILITY_STREAM_STATUS_2026-01-29.md` | 2026-01-30 | Phase 4/7/8 stream status: engine venv + baseline proofs; wizard upload + preflight; advanced panels + UI automation; build quality/warnings; SLO re-baseline + perf checks; Security Audit §9 CVE tracking |
| **TASK-0022 Evidence Pack** | `docs/reports/post_mortem/TASK-0022_EVIDENCE_PACK_2026-01-30.md` | 2026-01-30 | Enterprise-grade evidence catalog (E-001 to E-015), full missing file inventory (80+ files), minute-by-minute timeline for Git History Reconstruction incident |
| **Architecture Cross-Reference** | `docs/reports/verification/ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md` | 2026-01-30 | Full 9-domain comparison matrix of ChatGPT specs vs implementation; gap analysis; actionable integration plan; TD-013 to TD-016 identified |
| **Comprehensive Documentation Audit** | `docs/reports/audit/COMPREHENSIVE_AUDIT_FINAL_REPORT_2026-01-30.md` | 2026-01-30 | 8-phase audit: specs extraction, codebase inventory, doc completeness, spec-to-code xref, architecture compliance, restored modules, gap analysis, final report; 10 deliverables |
| **Final Sweep — Gaps and Realignment** | `docs/reports/audit/FINAL_SWEEP_GAPS_AND_REALIGNMENT_2026-01-30.md` | 2026-01-30 | Pre-realignment sweep: missing canonicals (MASTER_ROADMAP_UNIFIED, PROJECT_HANDOFF_GUIDE, ROLE_GUIDES_INDEX, architecture Part*.md, 13 ADRs), role/workflow/architecture gaps, recommended order of operations |
| **Forensic System Report** | `docs/reports/forensic/VOICESTUDIO_FORENSIC_SYSTEM_REPORT_2026-01-30.md` | 2026-01-30 | Comprehensive forensic analysis: 38-day period, 136 verification reports (98.5% pass), 101 proof runs (59.4% with failures), TASK-0022 S0 incident, 5 RCAs, 9 recommendations, installer error, 4 crash dumps, security audit |
| **Final Sweep Before Realignment** | `docs/reports/audit/FINAL_SWEEP_BEFORE_REALIGNMENT_2026-01-30.md` | 2026-01-30 | All-roles final sweep: missing/misaligned canonical files (PROJECT_HANDOFF_GUIDE, MASTER_ROADMAP_UNIFIED, DOCUMENT_GOVERNANCE, ROLE_GUIDES_INDEX, architecture README/Part series, 12 ADRs), scaffolding/architecture/workflow/role gaps; checklist for realignment and roadmap update |
| **Final Sweep (Missing & Never-Done)** | `docs/reports/verification/FINAL_SWEEP_MISSING_AND_NEVER_DONE_2026-01-30.md` | 2026-01-30 | Cross-role audit: missing roadmap/ADRs/governance/handoff/task system/architecture/production; TASK-0022 outstanding; backend/frontend gaps; realignment checklist |
| **Final Sweep — One Last Time** | `docs/reports/audit/FINAL_SWEEP_ONE_LAST_TIME_2026-01-30.md` | 2026-01-30 | **Authoritative** all-roles sweep before realignment: verified present (19 ADRs, handoff, roadmap, PRODUCTION_READINESS, role guides, AppServices, UseCases); still missing (Domain/Infrastructure in App, ARCHIVE_POLICY, GOVERNANCE_LOCK, templates/, Part*.md, TASK-0009/0011–0019 briefs, commit-discipline.mdc, BRANCH_MERGE_POLICY); structures/layers/role checklist; realignment checklist (§5) |
| **Final Sweep — All Roles (Pre-Realignment)** | `docs/reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md` | 2026-01-30 | **Authoritative** pre-realignment sweep: corrects record (what exists post–TASK-0022 vs missing); still-missing canonicals (ARCHIVE_POLICY, GOVERNANCE_LOCK, RULE_PROPOSAL_TEMPLATE, Part*.md, docs/archive/governance/); implementation/architecture gaps; checklist for realignment and roadmap update (§6) |
| **Final Sweep — Consolidated for Realignment** | `docs/reports/audit/FINAL_SWEEP_CONSOLIDATED_FOR_REALIGNMENT_2026-01-30.md` | 2026-01-30 | **Single reference** for all roles before realignment: corrected record (what exists vs missing); still-missing files; implementation/architecture gaps; role expectations; realignment checklist (§6). Use for plan/roadmap/role update. |
| **Phase 5 Observability Audit** | `docs/reports/audit/PHASE5_OBSERVABILITY_AUDIT_2026-02-05.md` | 2026-02-05 | Phase 5 completion audit: 15/15 tasks complete; OpenTelemetry, trace propagation, SLO dashboard, Prometheus export, diagnostics, error tracking; gate_status PASS, ledger_validate PASS |
| **Phase 6 Security Audit** | `docs/reports/audit/PHASE6_SECURITY_AUDIT_2026-02-05.md` | 2026-02-05 | Phase 6 completion audit: 7/7 tasks complete; HMAC request signing (40 tests), file validation by magic bytes (58 tests), dependency policy, Dependabot config, SBOM generation, CVE monitoring workflow, secrets rotation guide; 98 tests PASS |
| **v1.0.1 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.1.md` | 2026-02-05 | Phase 7 Production Readiness release: Installer enhancements (prerequisites, silent mode, upgrade validation), Error recovery (crash recovery, error reporting, data backup), Performance optimization (UI virtualization, lazy loading, response caching), Release documentation |
| **Core Workflow Audit** | `docs/reports/audit/CORE_WORKFLOW_AUDIT_2026-02-12.md` | 2026-02-12 | End-to-end workflow audit: Audio Import → Voice Cloning → Transcription → Playback; 35 issues (3 Critical, 7 High, 7 Medium, 5 Low, 6 Cross-workflow); 4-phase remediation roadmap; panel-by-panel feature matrix |
| **Panel Architecture Analysis** | `docs/reports/audit/PROFESSIONAL_PANEL_ARCHITECTURE_ANALYSIS.md` | 2026-02-14 | Moved from root (GAP-DOC-002). Professional-grade panel system analysis. |
| **Architecture Assessment and Remediation Plan** | `docs/reports/audit/ARCHITECTURAL_ASSESSMENT_AND_REMEDIATION_PLAN.md` | 2026-02-13 | Moved from root (GAP-DOC-002). Comprehensive architecture assessment. |
| **Complete System Report** | `docs/reports/VOICESTUDIO_COMPLETE_SYSTEM_REPORT.md` | 2026-02-13 | Moved from root (GAP-DOC-002). Full system capabilities report. |
| **Verify artifact audit (proof trail)** | `docs/reports/verify_artifact_audit_20260319.md` | 2026-03-19 | Why missing `verification_report.md`; Contract Tests timeout/logging; Write-Report paths |
| **Contract tests hang diagnosis** | `docs/reports/contract_tests_hang_diagnosis_20260319.md` | 2026-03-19 | Repro, buffering vs hang, Invoke-Stage notes |
| **Contract tests blocker** | `docs/reports/contract_tests_blocker_20260319.md` | 2026-03-19 | Headline blocker doc; harness + proof hygiene |
| **Contract tests failure inventory** | `docs/reports/contract_tests_failure_inventory_20260319.md` | 2026-03-19 | Enumerated former 18 failures + resolutions; fix order |
| **WorkflowModels corruption audit** | `docs/reports/workflowmodels_corruption_audit_20260321.md` | 2026-03-22 | Edit-integrity audit; JsonPropertyName incident; recurrence guards |

## Release

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Release Notes (Main)** | `docs/release/RELEASE_NOTES.md` | 2026-02-16 | Moved from root (GAP-DOC-002). Primary release notes document. |
| **v1.0.2 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.2.md` | 2026-02-21 | Plugin system infrastructure, Phase 4-10 deliverables. GA release. |
| **Handover Checklist** | `docs/release/HANDOVER_CHECKLIST.md` | 2026-02-21 | Build verification, test commands, gate status, key files, operational procedures, GA tag checklist |
| **v1.0.1 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.1.md` | 2026-02-05 | Phase 7 Production Readiness release. |

## Overseer Tooling

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Daily Workflow (Legacy) | `docs/archive/legacy_worker_system/overseer/DAILY_WORKFLOW_CHECKLIST.md` | 2026-01-30 | **ARCHIVED** — Legacy daily tasks; use ROLE_0_OVERSEER_GUIDE and PROJECT_HANDOFF_GUIDE |
| Gate Enforcement (Legacy) | `docs/archive/legacy_worker_system/overseer/GATE_ENFORCEMENT_GUIDE.md` | 2026-01-30 | **ARCHIVED** — Legacy gate guide; use Recovery Plan/QUALITY_LEDGER and run_verification.py |
| Handoff Process (Legacy) | `docs/archive/legacy_worker_system/overseer/HANDOFF_PROCESS_GUIDE.md` | 2026-01-30 | **ARCHIVED** — Legacy handoff; use HANDOFF_PROTOCOL.md and ROLE_GUIDES_INDEX |
| Quality Ledger | `Recovery Plan/QUALITY_LEDGER.md` | 2026-01-25 | VS-XXXX tracking |
| Verification automation | `scripts/run_verification.py`, `scripts/run-verification.ps1` | 2026-02-01 | Gate + ledger + completion guard (+ optional build, `--skip-guard` to bypass guard); proof in `.buildlogs/verification/last_run.json` |
| Overseer Issue System | `docs/developer/OVERSEER_ISSUE_SYSTEM.md` | 2026-02-02 | Unified issue logging from agents, engines, builds; recommendations and CLI for AI Overseer review; auto-task generation via `task_generator.py` |
| Issue-to-Task Generator | `tools/overseer/issues/task_generator.py` | 2026-02-02 | Automatic task brief creation from qualifying issues |
| Debug Agent Context Profile | `tools/context/config/roles/debug-agent.json` | 2026-02-02 | Context allocation weights/budgets for Debug Agent role |
| Telemetry API Routes | `backend/api/routes/telemetry.py` | 2026-02-02 | /api/telemetry/metrics, /api/telemetry/slos, /api/telemetry/spans endpoints |
| Onboarding Config | `tools/onboarding/config/onboarding.json`, `tools/onboarding/config/roles.json` | 2026-02-02 | Onboarding packet configuration and role registry |
| **Debug Role Integration** | `docs/developer/DEBUG_ROLE_INTEGRATION_GUIDE.md` | 2026-01-25 | Debug Role (Role 7) integration guide; issue-to-task workflow, escalation, CLI reference |

---

## Legacy Archive (Reference Only)

| Topic | Location | Notes |
| --- | --- | --- |
| Legacy Worker+Overseer System | `docs/archive/legacy_worker_system/` | 2026-01-30 — ChatGPT-era 3-Worker + Overseer docs; superseded by 8-role governance (ADR-003). See [README](../../archive/legacy_worker_system/README.md). |
| Outdated Dependency Status (PyTorch 2.9.0) | `docs/archive/dependencies/DEPENDENCY_STATUS_2025-01-28.md` | 2026-02-05 — References PyTorch 2.9.0+cu128 which was NOT adopted; production uses 2.2.2+cu121 per compatibility_matrix.yml. |

---

## Registry Maintenance

### Adding New Canonical Sources

1. Verify the topic doesn't already exist in this registry
2. Create the document following naming conventions in `DOCUMENT_GOVERNANCE.md`
3. Add an entry to the appropriate section above
4. Update the "Last Updated" date

### Superseding Documents

When a document is replaced:

1. Update this registry to point to the new canonical source
2. Add "Superseded by X" note to the old document
3. Move the old document to `docs/archive/{category}/`

### Disputes

If unclear which document is canonical:

1. Check this registry first
2. If not listed, check archive workflow in `docs/governance/DOCUMENT_GOVERNANCE.md` (ARCHIVE_POLICY.md not yet created; see [Final Sweep](../reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md) §6.1).
3. If still unclear, create an ADR to establish the canonical source
