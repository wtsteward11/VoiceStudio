# VoiceStudio Session State

**Role:** Session state oracle — phase, active task, Next 3 Steps, proof index. Not an archive or dashboard.

**Control doc roles:** `.cursor/STATE.md` (this file) = session context. `AGENTS.md` = rules + truth hierarchy. `CLAUDE.md` = architect prompt. `openmemory.md` = memory-first workflow; not architectural truth. Precedence when docs conflict: code → ADRs → CI → STATE → CLAUDE → conversation.

---

## Baseline Protection

- **Baseline Tag**: `v1.0.0-baseline`
- **Baseline Branch**: `baseline-2026-01-30`
- **Created**: 2026-01-30
- **Commit**: f5da3fd3

**To restore to baseline if needed:**

```bash
git checkout v1.0.0-baseline      # Detached HEAD at baseline
# OR
git checkout baseline-2026-01-30  # Branch at baseline
# OR
git reset --hard v1.0.0-baseline  # Reset current branch to baseline (destructive)
```

**Baseline includes:**

- 41 modern rules in `.cursor/rules/`
- 19 ADRs in `docs/architecture/decisions/`
- 8-role governance system complete
- validator_workflow.py, circuit breaker, pre-commit hooks
- CI verification integrated
- Legacy 886 files archived
- All gates B-H GREEN, verification PASS

---

## Current Phase

- **Phase**: v1.1.0 Completion Roadmap v2.0 — CI-Enforced Edition
- **Master Plan Phase**: Phase F COMPLETE — v1.1.0 Release
- **Started**: 2026-03-03
- **Context**: Roadmap v2.0 adopted. 7 ground truth gaps verified. 6 phases with CI-enforced gates. Phases 0, C, A, B, D, E, F complete. v1.1.0 shipped.

## Active Plan

- **Plan**: VoiceStudio Completion Roadmap v2.0 — CI-Enforced Edition
- **Document**: `docs/governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md`
- **Status**: COMPLETE — Phase F (v1.1.0 Release)
- **Previous Plan**: VoiceStudio 100% Completion Plan — COMPLETE (2026-02-26)

## Active Task

- **ID**: None
- **Title**: —
- **Status**: Awaiting selection

## Next 3 Steps

1. **Next migration target** — TemplateLibraryViewModel (Rank 7) per IBACKENDCLIENT_INSPECTION_TOP3.md. EffectsMixer deferred until lifecycle hardened. Run `python scripts/ci/generate_ibackendclient_queue.py` before trusting queue.
2. **Baseline hygiene** — Run creep check periodically; retained-async gate in run_verification; reduce .ci/retained_async_baseline.txt over time.
3. **Further lifecycle cleanup** — EffectsMixer (Task 3.2), VoiceCloningWizard, Library, Training (optional follow-through).

**Truth sync:** **SceneBuilderViewModel** — migration and lifecycle ownership complete (2026-03-13): OnActivatedAsync awaits LoadScenesAsync; staleness guard in LoadScenesAsync; IDispatcherTimer debounce; disposal stops timer. **BatchProcessingViewModel** — migration complete; lifecycle closed with accepted exceptions (polling/WebSocket retained; BATCH_PROCESSING_LIFECYCLE_PATTERNS.md).

**Hardening wave:** Closed. SceneBuilder lifecycle ownership complete. BatchProcessing polling/WebSocket retained by design. Migration queue may proceed after verifying unresolved targets against current code.

**Seam Migration Status:** SceneBuilderViewModel (Rank 16) — migration and lifecycle complete. AutomationViewModel (Rank 15), ScriptEditorViewModel (Rank 14), APIKeyManagerViewModel, BackupRestoreViewModel, GlobalSearchViewModel, MultiVoiceGeneratorViewModel, EnsembleSynthesisViewModel, MiniTimelineViewModel, TrainingDatasetEditorViewModel, BatchProcessingViewModel, DiagnosticsViewModel, TextSpeechEditorViewModel, AnalyzerViewModel, SettingsViewModel, MacroViewModel, ModelManagerViewModel, JobProgressViewModel — all migration-complete. Rank 11–16 wave closed.

## Last Milestone (TRUTH-RESET-LIFECYCLE)

- **ID**: TRUTH-RESET-LIFECYCLE
- **Title**: Truth Reset and Lifecycle Hardening Plan
- **Status**: **COMPLETE** (2026-03-13)
- **Completed**: Task 1: Truth reset (IBACKENDCLIENT_LONGTAIL_RANKING, STATE, SEAM_MATURITY_AUDIT aligned); Task 2: BatchProcessingViewModel lifecycle (_loadJobsCts for filter/project, _selectedJobLoadCts, BATCH_PROCESSING_LIFECYCLE_PATTERNS.md); Task 3: Re-ranked (MultiVoiceGenerator, EnsembleSynthesis, MiniTimeline); Task 4: Next Wave Hardening; Task 5: Lifecycle tests (OnFilterStatusChanged, OnSelectedProjectIdChanged, OnSelectedJobChanged); Task 6: VoiceSynthesisService retry policy documented.
- **Verification**: verify.ps1 -Quick, creep check, BatchProcessingViewModelSeamTests

**Proof Index (TRUTH-RESET-LIFECYCLE):**
| Date | Task | Artifact | Type | Status |
|------|------|----------|------|--------|
| 2026-03-13 | Truth Reset | check_ibackendclient_creep.py | Gate | PASS |
| 2026-03-13 | Lifecycle | BatchProcessingViewModelSeamTests (9) | Test | PASS |
| 2026-03-13 | Lifecycle | BATCH_PROCESSING_LIFECYCLE_PATTERNS.md | Doc | Added |
| 2026-03-13 | SceneBuilder lifecycle | SceneBuilderViewModel.cs (a1dfafe9) | Code | PASS |

**Previous:** NEXT10-SEAM-BATCH (2026-03-13)

**Previous:** WAVE2-LIFECYCLE-FOLLOW-THROUGH (2026-03-13)

**Proof Index (NEXT10-SEAM-BATCH):**
| Date | Task | Artifact | Type | Status |
|------|------|----------|------|--------|
| 2026-03-13 | NEXT10 | verify.ps1 -Quick | Gate | PASS |
| 2026-03-13 | NEXT10 | check_ibackendclient_creep.py | Gate | PASS |
| 2026-03-13 | NEXT10 | JobProgressViewModelTests, JobProgressModelTests (29) | Test | PASS |
| 2026-03-13 | NEXT10 | dotnet build | Gate | PASS |

## Prior Milestone (WAVE2-LIFECYCLE-FOLLOW-THROUGH)

- **ID**: WAVE2-LIFECYCLE-FOLLOW-THROUGH
- **Title**: Wave 2 Lifecycle Follow-Through and Wave 3 Preparation
- **Status**: **COMPLETE** (2026-03-13)
- **Completed**: WAVE2_LIFECYCLE_AUDIT; BatchProcessing lifecycle (_disposalCts, _selectedJobLoadCts, silent catches fixed); VoiceCloningWizard LoadEnginesAsync moved to Loaded; Library loads moved to OnActivatedAsync; Wave 3 re-ranking (RealTimeVoiceConverter primary); VoiceSynthesisService (no mutation, BackendNotFoundException); seam-aware tests (BatchProcessing, VoiceCloningWizard, VoiceSynthesisService).
- **Verification**: verify.ps1 -Quick, creep check, seam tests

**Proof Index (WAVE2-LIFECYCLE-FOLLOW-THROUGH):**
| Date | Task | Artifact | Type | Status |
|------|------|----------|------|--------|
| 2026-03-13 | WAVE2-LIFECYCLE | verify.ps1 -Quick | Gate | PASS |
| 2026-03-13 | WAVE2-LIFECYCLE | SeamTests (VoiceSynthesis, BatchProcessing, VoiceCloningWizard) | Test | PASS |

---

**Known Debt:** TrainingViewModel lifecycle fire-and-forget: LoadLogsAsync/LoadQualityHistoryAsync now gated (selection-specific cancellation + staleness guard); ConnectWebSocketAsync, LoadDatasetsAsync, LoadTrainingJobsAsync, PollTrainingStatusAsync, DisconnectWebSocketAsync retained. _disposalCts cancelled in Dispose. **Decision (2026-03-13):** Training remains explicit exception model (documented in [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](docs/design/TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)); full lifecycle cleanup deferred unless requested.

---

**Archive:** Previous milestones, proof index, and session log → [docs/governance/STATE_ARCHIVE.md](../docs/governance/STATE_ARCHIVE.md)
