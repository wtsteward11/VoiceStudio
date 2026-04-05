# VoiceStudio Edit-Apply Retry Recovery Lane Closure — 2026-04-01

**Lane:** GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01 (session-local retry for failed transcript apply/regenerate job rows; frozen snapshot; no new backend routes)  
**Execution row:** [GOV_VOICESTUDIO_EDIT_APPLY_RETRY_RECOVERY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_EDIT_APPLY_RETRY_RECOVERY_01_EXECUTION_ROW.md)  
**Product:** **GAP-045** remains **Open**; this lane is a bounded sub-lane only.

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — full matrix below executed on a developer machine with normal repo/toolchain access (not connector-limited for this closure).

## 1) Scope summary

- **`TranscriptApplyJobStatusEntry`:** Retry snapshot fields (`TranscriptionId`, `ProjectId`, `ReplacementTextSnapshot`, `RangeEndInclusiveIndex`, anchor start/end, existing `ClipId`); `CanShowRetry`; status line hint.
- **`TranscribeViewModel`:** Snapshot populated on every job row (including coordinator-missing preflight); `RetryTranscriptApplyJobAsync` fail-closed preflight + replay via `RegenerateSegmentAudioAsync`.
- **UI:** `TranscribeView` per-row **Retry** (`TranscribeView_ApplyJobRetryButton`); code-behind routes to VM.
- **Tests:** `TranscribeViewModelInlineEditTests` (retry replay + stale timing + transcription mismatch); `TranscriptSegmentRegenerationCoordinatorTests.TryExecuteAsync_SecondInvocation_WithNewCorrelationId_StillSucceeds`; `TranscriptApplyJobStatusMapperTests` (`cancelled`, `apply_failed`); `TranscribeViewModelRegenerateSnapshot` assertions on failed row.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in other files) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **2979** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **216** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260401_193939/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260401-194441**) |

## 3) Proof artifacts (code)

- `src/VoiceStudio.App/Services/TranscriptApplyJobStatusModels.cs`
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/TranscribeView.xaml` + `.xaml.cs`
- `src/VoiceStudio.App/Constants/AutomationIds.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelRegenerateSegmentTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptApplyJobStatusMapperTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptSegmentRegenerationCoordinatorTests.cs`

## 4) Honest limits

- **In lane:** Session-only retry; no persisted queue; retry uses snapshot only; no FastAPI surface change.
- **Still Open (GAP-045):** Broader document-class editor and remaining tracker narrative — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).

## 5) Connector / oversight (424/502)

- **Tracked** for environments where Project Access / connector instability blocks verification; use execution row §10 + `QUALITY_LEDGER.md` **Oversight — IDE connector** note. This closure run was **not** blocked.

## 6) Closure

**GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01:** **Closed** 2026-04-01 with proof-backed acceptance per execution row.

**GAP-045:** remains **Open** — this lane closes the **operator retry/recovery for failed apply/regenerate jobs** slice only.
