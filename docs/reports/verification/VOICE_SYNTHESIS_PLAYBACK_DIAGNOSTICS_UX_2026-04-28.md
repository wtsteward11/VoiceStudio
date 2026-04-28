# Voice Synthesis Playback Diagnostics UX — Verification

**Date:** 2026-04-28  
**Scope:** When generated audio exists but playback fails, the Voice Synthesis panel shows playback-specific diagnostics (`InfoBar`), preserves synthesis result state, and offers **Retry**, **Copy error**, and existing **Copy Reference** where applicable. Playback failures no longer set generic `HasError` / synthesis error `InfoBar` (non-playback synthesis errors do not show playback diagnostics).

## Files changed

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `IsPlaybackError`, `PlaybackErrorMessage`, `PlaybackErrorDetails`, `PlaybackErrorAudioId`, `PlaybackErrorAudioReference`, `ShowPlaybackError`, `RetryPlaybackCommand`, `CopyPlaybackErrorCommand`, `DismissPlaybackError`, `PlayAudioAsync` playback-only error path; `Play` guard allows audio-ID-only when URL empty; `BeginSynthesisOperationNarrativeHygiene` clears playback error.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` — Row for playback error `InfoBar`; synthesis error row shifted; overlay `RowSpan` 9.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml.cs` — `PlaybackErrorInfoBar_Closed` → `DismissPlaybackError()`.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — 11 playback diagnostics tests.
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — `VoiceSynthesisView_PlaybackErrorInfoBar`, `VoiceSynthesisView_RetryPlaybackButton`, `VoiceSynthesisView_CopyPlaybackErrorButton`.

## Behavior

- Playback exception: sets playback fields, not `HasError`; optional toast only (no blocking error dialog for this path).
- Success / retry success: `ClearPlaybackError()`.
- **Copy Playback Error** builds multi-line diagnostic text (message, details, audio id, reference) via existing clipboard helper.

## Tests

- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"`
- **Result:** Passed **112**, Skipped 22 (other filters), **0** failed (session run).

## Verification artifact

- `python scripts/run_verification.py` — **Overall: PASS** → `.buildlogs/verification/last_run.json`
- `.\scripts\verify.ps1 -Quick` — exit **0** → `artifacts/verify/20260428_003444/verification_report.md`

## Known limitations

- Headless tests mock `IAudioPlayerService` / `GetAudioStreamAsync`; no real audio device or WinUI automation required.
- Clipboard not asserted in unit tests; commands and state are covered.

## Explicit

- **Not** GAP-008 / **not** `MainWindow*ShellBridge` / **not** Slice 46.
- **Not** RHVoice; did not edit `ENGINE_PARITY_MATRIX.md`.
- **Not** runtime FULL PASS; no manual in-app heard attestation.
