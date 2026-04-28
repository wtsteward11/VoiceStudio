# Voice Synthesis — Timeline placement stability bundle (2026-04-28)

## Scope

Stabilize **generated-audio timeline placement** after the output-to-timeline feature: deterministic start-time resolution, **fail-closed** behavior when track clip payloads are missing or unusable (no silent overlap at `0`), explicit result kinds, VM status semantics, and automated tests.

## Selected placement policy

| Condition | Result | `CreateClipAsync` |
|-----------|--------|-------------------|
| `track.Clips == null` | `PlacementUnavailable` + actionable message | **No** |
| `track.Clips` empty list | `DefaultAtZeroBecauseTrackEmpty` at **0 s** | **Yes** |
| ≥1 valid clip (`StartTime` finite, ≥ 0; `Duration` > 0) | `ExactAppend` at **max valid clip end** | **Yes** |
| Mixed valid + invalid clips | Invalid ignored; append at max valid end | **Yes** |
| All clips invalid | `PlacementUnavailable` | **No** |

No arbitrary gap was added beyond existing clip end times.

## Files changed

- `src/VoiceStudio.App/Services/IGeneratedAudioTimelineService.cs` — `GeneratedAudioTimelineKind` (`ExactAppend`, `DefaultAtZeroBecauseTrackEmpty`, `PlacementUnavailable`); `GeneratedAudioTimelineResult.PlacementStartSeconds`.
- `src/VoiceStudio.App/Services/GeneratedAudioTimelineService.cs` — `TryResolvePlacement` / `TryGetValidClipEndSeconds`.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — success mapping + `PlacementUnavailable` warning path.
- `src/VoiceStudio.App.Tests/Services/GeneratedAudioTimelineServiceTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs`

## Tests run

- `dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GeneratedAudioTimelineServiceTests|FullyQualifiedName~VoiceSynthesisViewModelTests"` → **162 passed** (0 failed).

## Verification artifacts

- `python scripts/run_verification.py` → **Overall: PASS**; JSON: `.buildlogs/verification/last_run.json`
- `.\scripts\verify.ps1 -Quick` → **exit 0**; report: `artifacts/verify/20260428_135621/verification_report.md`

## Known limitations

- `ITimelineTrackService.GetTracksAsync` does not guarantee hydrated clip lists; placement **fail-closed** when clips are null or all timing is invalid (user must open Timeline / refresh as directed by messages).
- Mixed valid/invalid clips: invalid clips are ignored without extra UI copy (no separate “ignored clips” banner unless extended later).

## Explicit non-claims

- **Not** GAP-008 / **not** `MainWindow*ShellBridge` / **not** Slice 46.
- **Not** RHVoice / **not** `ENGINE_PARITY_MATRIX` edits.
- **Not** a **runtime FULL PASS** or in-app human attestation.
- Prior verified commit **`db1fe6a7`** (output-to-timeline) was pushed when guards passed; **this** bundle’s implementation commit is **local-only** per lane instructions (not pushed).
