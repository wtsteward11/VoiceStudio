# Voice Synthesis UI — Piper path completion (2026-04-27)

**Scope:** WinUI **Voice Synthesis** panel hardening for Piper-related flows: HTTP **403** → typed `ConsentRequiredException`, actionable copy via `ActionableErrorTranslator`, playback **StatusMessage** before `PlayFileAsync`, and **8** new `VoiceSynthesisViewModel` unit tests.

**Not in scope (honored):** GAP-008 / Slice 46 / `MainWindow*ShellBridge` / `MainWindow` refactor / RHVoice / `ENGINE_PARITY_MATRIX.md`.

## Files changed

| Area | File |
|------|------|
| Exceptions | `src/VoiceStudio.App/Core/Exceptions/BackendException.cs` — `ConsentRequiredException` |
| HTTP mapping | `src/VoiceStudio.App/Services/BackendClientHttpPipeline.cs` — `403` → `ConsentRequiredException` |
| Actionable copy | `src/VoiceStudio.App/Utilities/ActionableErrorTranslator.cs` — consent branch |
| ViewModel | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `Status.PlayingAudio` before each `PlayFileAsync` path |
| Tests | `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — 8 new tests |

**Note:** `OnWorkflowStateChanged` already used `nameof(CanPlayAudio)` (no `CCanPlayAudio` typo).  
**Note:** `BackendClient.SynthesizeVoiceAsync` uses shared `_pipeline.CreateExceptionFromResponseAsync`; **403** mapping is centralized in `BackendClientHttpPipeline` (same effect as per-route mapping).

## Acceptance criteria

- [x] Consent failures surface as **`ConsentRequiredException`** from HTTP **403** and actionable UI message (consent wording).
- [x] Playback shows **Playing…** status before awaiting `IAudioPlayerService.PlayFileAsync` (both stream and URL download paths).
- [x] Eight tests: play gating, success storage, playback service invocation, consent + generic errors, Piper `CanSynthesize`, error state does not enable play.

## Verification (automated)

| Command | Result |
|---------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | 0 errors |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis" --no-build` | Passed: 82 (E2E UI tests skipped) |
| `python scripts/run_verification.py` | Overall: **PASS** → `.buildlogs/verification/last_run.json` |
| `.\scripts\verify.ps1 -Quick` | **VERIFICATION PASSED** |

**Report:** `artifacts/verify/20260427_212919/verification_report.md`  
**JSON:** `.buildlogs/verification/last_run.json`

## Explicit limitations

- **Not** a claim of full **runtime** product PASS for end-to-end Piper in the live app; this slice is **code + unit tests + verify harness**.
- **Manual** in-app synthesis/playback smoke was **not** performed in this session (per runtime-truth **PARTIAL** posture in **STATE**).

## Related

- Runtime follow-up doc: [VOICESTUDIO_RUNTIME_TRUTH_FOLLOWUP_WINUI_PIPER_2026-04-27.md](./VOICESTUDIO_RUNTIME_TRUTH_FOLLOWUP_WINUI_PIPER_2026-04-27.md)  
- Control plane: [.cursor/STATE.md](../../.cursor/STATE.md) **ACTIVE WINDOW** (GAP-008 freeze unchanged).
