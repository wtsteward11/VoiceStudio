# VoiceStudio Session State

**Role:** Session state oracle. Zone 1 (ACTIVE WINDOW) = current execution truth. Zone 2 (HISTORY LEDGER) = historical context. Agents read Zone 1 only unless explicitly told to read history.

**Control doc roles:** Code → ADRs → CI → STATE (Zone 1) → CLAUDE → conversation.

---

## ACTIVE WINDOW

Read only this section as current task truth. Treat everything below the divider as historical context.

### Active Task
- **ID:** VOICEBROWSER-TESTS
- **Title:** VoiceBrowserViewModelTests fix for IVoiceBrowserClient
- **Status:** Complete

### Next 3 Steps
1. Pick next migration target from regenerated queue (Top 3: VoiceQuickCloneViewModel, WorkflowAutomationViewModel)
2. Inspect call sites per Top 3 sheet before coding
3. Run verify.ps1 -Quick before any code changes

### Current Target
VoiceQuickCloneViewModel — IVoiceQuickCloneClient; inspect call sites per Top 3 sheet before coding

### Current Blocker
None

### Truth Sync Note
VoiceMorphingBlendingViewModel migrated (2026-03-14). VoiceBrowserViewModelTests updated for IVoiceBrowserClient (mock IVoiceBrowserClient, VoiceSearchResponse/LanguagesResponse/TagsResponse from VoiceStudio.App.Services). 61 migrated ViewModels; 60 with constructor invariant.

### Last Verified Commands
- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — PASS (2026-03-14)
- `dotnet test ... --filter "FullyQualifiedName~VoiceBrowserViewModelTests"` — 17 passed
- `python scripts/run_verification.py` — PASS after commit (completion_guard requires committed markers)
- `python scripts/ci/check_ibackendclient_creep.py` — PASS

### Context Acknowledgment
2026-03-14 — Bulletproof Plan: Phase 0–5 complete. TextSpeechEditorViewModel FAF fixed; EngineParameterTuningViewModel migrated; AppServices decomposed; STATE and queue docs updated.

---
## HISTORY LEDGER
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

**Baseline includes:** 41 modern rules, 19 ADRs, 8-role governance, validator_workflow.py, circuit breaker, pre-commit hooks, CI verification integrated, legacy 886 files archived, all gates B-H GREEN.

## CURRENT POSITION
- **Phase:** v1.1.0 Completion Roadmap v2.0 — Phase F COMPLETE
- **Plan:** VOICESTUDIO_COMPLETION_ROADMAP_V2.md
- **Known Debt:** TrainingViewModel lifecycle FAF (documented in TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md; deferred)

## LATEST MILESTONE
- **ID:** BULLETPROOF-PLAN
- **Title:** VoiceStudio Bulletproof Completion Plan — Phases 0–5
- **Status:** COMPLETE (2026-03-14)
- **Completed:** Phase 0 (TextSpeechEditorViewModel FAF, constructor invariant gate, audit); Phase 1 (ADR-050 golden path, retained-async baseline); Phase 2 (queue refresh, EngineParameterTuningViewModel migration); Phase 3 (AppServices RegisterPanelServices); Phase 4 (wedge doc, cross-panel); Phase 5 (STATE, queue).
- **Verification:** constructor_invariant in run_verification.py; 44 migrated, 43 with invariant; EngineParameterTuningViewModelSeamTests pass

## LATEST PROOF INDEX
| Date | Task | Artifact | Type | Status |
|------|------|----------|------|--------|
| 2026-03-14 | Bulletproof 0.1 | TextSpeechEditorViewModel IPanelLifecycle, no constructor FAF | Code | Done |
| 2026-03-14 | Bulletproof 0.2 | run_verification.py constructor_invariant check | Gate | PASS |
| 2026-03-14 | Bulletproof 2.2 | EngineParameterTuningViewModelSeamTests | Test | 3 passed |
| 2026-03-14 | ImageGen migration (Rank 13) | ImageGenViewModelSeamTests | Test | 3 passed |
| 2026-03-14 | Spectrogram migration (Rank 14) | SpectrogramViewModelSeamTests | Test | 3 passed |
| 2026-03-14 | SpatialAudio migration (Rank 15) | SpatialAudioViewModelSeamTests | Test | Pass |
| 2026-03-14 | PluginHealth migration (Rank 19) | PluginHealthDashboardViewModelSeamTests | Test | 3 passed |
| 2026-03-14 | ProfileHealth migration (Rank 20) | ProfileHealthDashboardViewModelSeamTests | Test | 4 passed |
| 2026-03-14 | Lexicon migration (Rank 17) | LexiconViewModelSeamTests | Test | Added |
| 2026-03-14 | TodoPanel migration (Rank 18) | TodoPanelViewModelSeamTests | Test | Added |
| 2026-03-14 | HelpViewModel migration | IHelpClient, HelpClient, HelpViewModelSeamTests | Code | Done |
| 2026-03-14 | EmotionStylePresetEditorViewModel migration | IEmotionControlClient.UpdatePresetAsync, EmotionStylePresetEditorViewModelSeamTests | Code | Done |
| 2026-03-14 | KeyboardShortcutsViewModel migration | IKeyboardShortcutsClient, KeyboardShortcutsClient, KeyboardShortcutsViewModelSeamTests | Code | Done |
| 2026-03-14 | PronunciationLexiconViewModel migration | IPronunciationLexiconClient, PronunciationLexiconClient, IVoiceSynthesisService, PronunciationLexiconViewModelSeamTests | Code | Done |
| 2026-03-14 | ProsodyViewModel migration | IProsodyClient, ProsodyClient, ILifecyclePanelView, ProsodyViewModelSeamTests (3 passed) | Code | Done |
| 2026-03-14 | TagManagerViewModel migration | ITagManagerClient, TagManagerClient, ILifecyclePanelView, TagManagerViewModelSeamTests (3 passed) | Code | Done |
| 2026-03-14 | MarkerManagerViewModel migration | IMarkerManagerClient, MarkerManagerClient, ILifecyclePanelView, MarkerManagerViewModelSeamTests (3 passed) | Code | Done |
| 2026-03-14 | VoiceMorphingBlendingViewModel migration | IVoiceMorphingBlendingClient, VoiceMorphingBlendingClient, VoiceMorphingBlendingViewModelSeamTests (3 passed) | Code | Done |
| 2026-03-14 | VoiceBrowserViewModelTests fix | IVoiceBrowserClient mock, VoiceSearchResponse/LanguagesResponse/TagsResponse from Services; 17 tests pass | Test | Done |
| 2026-03-14 | Bulletproof 3.1 | AppServices RegisterPanelServices | Code | Done |
| 2026-03-14 | Bulletproof 5.1 | STATE.md, IBACKENDCLIENT_UNRESOLVED_QUEUE.md | Doc | Updated |

## COMPLETED MILESTONES
- TRUTH-RESET-LIFECYCLE, NEXT10-SEAM-BATCH, WAVE2-LIFECYCLE-FOLLOW-THROUGH
- See [STATE_ARCHIVE.md](docs/governance/STATE_ARCHIVE.md) for older milestones

## PROOF HISTORY
See [STATE_ARCHIVE.md](docs/governance/STATE_ARCHIVE.md) for older proof indexes.

## ARCHIVE POINTER
[docs/governance/STATE_ARCHIVE.md](docs/governance/STATE_ARCHIVE.md)
