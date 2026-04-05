# VoiceStudio Regenerate Segment Lane Closure — 2026-03-31

**Lane:** GOV-VOICESTUDIO-REGENERATE-SEGMENT-01 (**GAP-046** — transcript-driven single-segment regeneration: job → clip apply → linkage removal → undo → timeline event)  
**Execution row:** [GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md)

## 1) Scope summary

- **Backend:** `POST /api/transcribe/regenerate-segment` with `_validate_regenerate_segment_request`; worker `backend/services/transcript_segment_regeneration.py`; `PUT .../clips/{clip_id}` with `ArtifactRefCounter` increment/decrement on `audio_id` swap (`backend/api/routes/tracks.py`).
- **App:** `TranscriptSegmentRegenerationCoordinator`, `TranscriptClipAudioReplaceUndoAction`, `ClipAudioArtifactReplacedEvent`, `TimelineViewModel` apply + project sync, `TranscribeViewModel.RegenerateSegmentAudioAsync`, segment context menu, DI in `AppServices.cs`.
- **Tests:** `tests/unit/backend/api/routes/test_transcribe_regenerate.py` (route validation + 202 stub path); `tests/unit/backend/api/routes/test_tracks_clip_update.py` (ref-count + clip fields); `TranscriptSegmentRegenerationCoordinatorTests`; `TranscriptClipAudioReplaceUndoActionTests`; `TimelineViewModelGap046EventTests`; `TranscribeViewModelRegenerateSegmentTests` (null-coordinator message; execution path covered by coordinator tests to avoid global `AppServices` replacement flaking downstream tests). **CI:** `ClipAudioArtifactReplacedEvent` added to [PANEL_WIRING_CATALOG.md](../../design/PANEL_WIRING_CATALOG.md) allowlist.
- **Helper:** `TestAppServicesHelper.RebuildDefaultProvider()` for tests that replace `AppServices` with a minimal provider that still satisfies `EnsureInitialized()` early-return checks (documented for future use).

## 2) Verification matrix (closure run)

| Command | Result |
|--------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build --logger "console;verbosity=minimal"` | PASS — **2908 passed**, **278 skipped**, **0 failed** |
| `python -m pytest tests/unit/backend/api/routes/test_transcribe_regenerate.py tests/unit/backend/api/routes/test_tracks_clip_update.py -v` | PASS — **14 passed** |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **216 passed**, **2 deselected** |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260331_053404/verification_report.md` (internal gate stage may use `--skip-guard`; see below) |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` |

**Authoritative completion_guard:** `python scripts/run_verification.py` after committed closure markers (per repository closure protocol).

## 3) Proof artifacts (code)

- `backend/api/routes/transcribe.py`, `backend/services/transcript_segment_regeneration.py`, `backend/api/routes/tracks.py`
- `src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs`, `TranscriptClipAudioReplaceUndoAction.cs`, `TranscriptRegenerationClient.cs` (as registered)
- `src/VoiceStudio.Core/Events/PanelEvents.cs` — `ClipAudioArtifactReplacedEvent`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs`, `TranscribeViewModel.cs`, `TranscribeView.xaml.cs`
- Tests listed in §1; `docs/design/PANEL_WIRING_CATALOG.md` allowlist entry

## 4) Honest limits

- **In scope (this lane):** One segment → one resolved clip; fail-closed resolution before API; single-segment regeneration only; canonical job + clip `PUT` + ref-count; linkage removal after success; undo restores prior clip audio + linkage rows; timeline observable sync via event.
- **Out of scope:** Bulk regeneration, Descript-class editing, new job subsystem, transport changes, full **GAP-045** product row (advanced text editing / multi-segment / polish). **GAP-045** remains **Open** for that broader vision; **GAP-046** execution lane is **Closed**.

## 5) Closure

**GOV-VOICESTUDIO-REGENERATE-SEGMENT-01:** **Closed** 2026-03-31 with binary acceptance checked on the execution row and governance surfaces synced (this report, tracker, registry, `STATE.md`).

**GAP-046:** **Closed** in [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).
