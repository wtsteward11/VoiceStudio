# GOV-VOICESTUDIO-GAP045-SUBTITLE-RESTORE-PROJECT-LIFECYCLE-HYGIENE-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-GAP045-SUBTITLE-RESTORE-PROJECT-LIFECYCLE-HYGIENE-01`  
**Status:** **Closed** (2026-04-06) — bounded slice; product **GAP-045** remains **Open**.  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_01_EXECUTION_ROW.md) (**Closed**)  
**Closure:** [VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_LANE_CLOSURE_2026-04-06.md)

## Problem statement

`LastSubtitleTranscriptionId` is persisted per project JSON file. Project **identity** transitions (New, Save As with new id, Open another project) must not carry subtitle-restore metadata or **in-memory** `SelectedTranscription` from a prior project into the next project’s rehydrate path. Without explicit hygiene, Save As could inherit a restore id if the shell ever deep-copies `Project`, and Transcribe could prefer a stale in-memory transcription id over the new project’s stored restore id.

## Frozen architecture decisions

1. **New project (command path):** `Project` shell for a new identity sets `LastSubtitleTranscriptionId = null` explicitly (no implicit default reliance).
2. **Save As (command path):** New identity receives a new `Project.Id`; **`LastSubtitleTranscriptionId` is not copied** from the source project (fail-closed: null on the saved-as project).
3. **Open (command path):** Loaded project is authoritative; no merge of prior handler in-memory `Project` fields into the opened file.
4. **Transcribe panel:** On `SelectedProjectId` change, clear in-memory transcript selection (and related UI list state) so `RunBackendTranscriptRehydrateAsync` does not treat the previous project’s `SelectedTranscription` as `previousSelectionId` when the new project should use repository restore or list default.
5. **Timeline / coordinator path:** Backend `CreateProjectAsync` remains authoritative for menu-driven new projects; DTO is not expected to carry local JSON-only fields; no change unless a future row unifies local JSON with shell create.
6. **Validity authority unchanged:** Backend `ListTranscriptionsAsync` remains the only validity source for restore; stale stored id behavior stays per prior row.

## Acceptance contract (all required)

- [x] `FileOperationsHandler` New + Save As produce projects with `LastSubtitleTranscriptionId == null`.
- [x] Save As from a project that had a non-null `LastSubtitleTranscriptionId` persists a new project file without that field carrying over.
- [x] `TranscribeViewModel`: changing `SelectedProjectId` clears stale `SelectedTranscription` / list state so repository-backed restore can apply for the new project.
- [x] `JsonProjectRepository` round-trip tests for `SaveLastSubtitleTranscriptionIdAsync` / `GetLastSubtitleTranscriptionIdAsync`.
- [x] Seam tests: extended `FileOperationsHandlerTests`, `TranscribeViewModelLastSubtitleRestoreTests`, `JsonProjectRepositoryTests` (Timeline project-switch hygiene covered by existing `TimelineViewModelGap045CrossConsumerTests`).
- [x] Closure matrix + governance sync (STATE / tracker / registry / proof index).

## Diagnostics

- **Log (optional):** structured info when clearing transcript UI state on project id change (debug/diagnostic channel only; no new operator toast required for this lane).
- **Operator copy:** unchanged from prior row for stale restore id (`[Restore] Last subtitle transcription no longer exists…`).

## Hard OUT

- Broad project-format redesign or schema bump beyond additive null semantics already in place.
- New backend routes or SQLite fields for `LastSubtitleTranscriptionId`.
- Unifying `FileOperationsHandler` and `TimelineViewModel` create paths in one row (document only if divergence remains).
- Startup / cold-launch-only scope creep.

## Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted `dotnet test` — filter lifecycle + last-subtitle + JsonProjectRepository
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage: `UI Self-Test`, `Icon-Launch Smoke`, `Failure-Path Smoke`, `Runtime-Missing Failure Smoke`

## Rollback

Revert `FileOperationsHandler` lifecycle helper + `TranscribeViewModel.OnSelectedProjectIdChanged` hygiene, new/extended tests, execution row, closure report, and governance deltas for this lane only.

## Changelog

- **2026-04-06:** Row frozen (Open) — lifecycle hygiene for `LastSubtitleTranscriptionId` + Transcribe project-switch selection clearing.
- **2026-04-06:** Row **Closed** — matrix + closure report [VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_LANE_CLOSURE_2026-04-06.md); App.Tests **3097**/skipped **274**; Quick **20260406_001747**; rolling **20260406-002616**; OnlyStage **002301** / **002311** / **002319** / **002336**.
