# GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01 — Slice 3 proof (2026-03-28)

**Scope:** Slice 3 only — playhead, seek, stop, preview, and context transport **policies** per [GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md) §7. **Slice 4 not claimed** (proof matrix + full lane closure).

## Canonical time (frozen)

- **`IAudioPlayerService`** supplies low-level playback position; **`PositionChanged`** updates the VM during playback.
- **`CurrentPlaybackPosition`** is the single VM time source for **`TransportTimeDisplay`**, **`PlayheadPosition`**, and dependent **`IsPlayheadVisible`** notifications (`OnCurrentPlaybackPositionChanged`).
- **Seek:** `SeekToPosition` calls **`_audioPlayer.Seek(timeInSeconds)`** and sets **`CurrentPlaybackPosition = timeInSeconds`** in the same method.

## Stop policy (frozen)

- After **`StopAudio()`** calls **`_audioPlayer.Stop()`** and sets **`IsPlaying = false`**, the VM sets **`CurrentPlaybackPosition = 0.0`** so transport time and playhead do not retain a stale tick when **`PositionChanged`** does not fire to zero (reader disposal).
- **UX note:** This is **not** DAW-style “stop preserves playhead”; it is intentional deterministic reset for the current product maturity (documented in execution row §7).

## Preview policy (frozen)

- Preview uses separate NAudio instances; it does not drive **`PositionChanged`** on the main path.
- **`TimelineScrubCanvas_PointerReleased`**: after **`StopPreview()`**, the code-behind sets **`ViewModel.IsPreviewing = false`** so the VM does not stay stuck **`true`** if the preview completion callback never runs (race with cancel).

## Context / `SetCurrentPlayable` (honesty)

- **`SetCurrentPlayable`** is invoked when timeline play starts (identity: audio id, source, title). **Seek / pause / resume** do not need a new call — playable identity is unchanged.
- **`CurrentPlayableSource`** and timeline ownership follow **last-writer-wins** and are **not** cleared on timeline stop (aligned with Library/Synthesis). **Frozen policy**, not a Slice 3 defect.

## Code touched

| Area | File | Change summary |
|------|------|----------------|
| Timeline VM | `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` | **`StopAudio`:** `CurrentPlaybackPosition = 0.0` after stop |
| Timeline view | `src/VoiceStudio.App/Views/Panels/TimelineView.xaml.cs` | **Scrub release:** `ViewModel.IsPreviewing = false` after `StopPreview()` |
| Lane doc | `docs/design/GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md` | §7 Slice 3 contract + changelog |

## Tests (`TimelineViewModelTests`)

| Test | Intent |
|------|--------|
| `TransportTimeDisplay_FormatsCurrentPlaybackPosition_Deterministically` | Time display + **`PlayheadPosition`** at 61.5s (zoom 1) |
| `SeekToPositionCommand_CallsPlayerSeek_AndSetsCurrentPlaybackPosition` | Pixels → seconds; **`Seek(50)`** + position 50 |
| `StopAudioCommand_ResetsCurrentPlaybackPosition_AndTransportTimeDisplay` | Stop → **0.0** + **`00:00.000`** |
| `StopAudioCommand_WhenStopped_IsPlayheadVisible_IsFalse` | No play / preview / player-playing → playhead hidden |
| `IsPreviewing_TogglesPlayheadVisibility_AndPlayheadPulsing` | Preview flag isolation |
| `CurrentPlaybackPosition_WhenChanged_RaisesPlayheadPosition_IsPlayheadVisible_TransportTimeDisplay` | Dependent **`PropertyChanged`** names |

## Verification (executed)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `dotnet test` … `TimelineViewModelTests` (filter) | PASS (44 tests in class, 0 failed) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (216 passed, 2 deselected) |
| `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260328_060039/verification_report.md` |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS); JSON `.buildlogs/verification/last_run.json` |

**Note:** A subsequent ad-hoc full **`dotnet test`** run hit **MSB3027** file locks (**testhost** holding DLLs) when overlapping another process; authoritative full-suite signal for this slice is **`verify.ps1 -Quick`** (exit 0) plus the targeted **`TimelineViewModelTests`** run above.

## Next

**Slice 4:** proof matrix + lane closure report — per execution row. **Lane remains Open** until Slice 4 completes.
