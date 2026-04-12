# VOICESTUDIO — GAP-067 Jump Lists (Slice 2) — Lane Closure

**Date:** 2026-04-12  
**Lane:** GOV-VOICESTUDIO-GAP067-JUMP-LISTS-02  
**Execution row:** [GOV_VOICESTUDIO_GAP067_JUMP_LISTS_02_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP067_JUMP_LISTS_02_EXECUTION_ROW.md) — **Closed**

## Summary

Bounded slice 2 delivers **unpackaged-safe** Windows taskbar jump lists via Win32 COM **`ICustomDestinationList`** (not MSIX-only `Windows.UI.StartScreen.JumpList`). **`JumpListService`** projects **`RecentProjectsService.AllProjects`** (single source of truth; cap 10; debounced refresh). Static tasks **New Project** / **Open Project** use `--jumplist-new` and `--jumplist-open-dialog`; recents use `--jumplist-open "{path}"`. **`JumpListActivation`** parses launch args; **`MainWindow`** consumes pending actions after **`IStartupStateService.IsReady`**. Tests: **`Gap067Slice2Tests`** (11 source-contract) + **`JumpListServiceSeamTests`** (6).

## Hardening note (full-suite stability)

A full **`VoiceStudio.App.Tests`** run surfaced a **flaky** failure in `TranscribeViewModelInlineEditTests.RangeApply_Failure_DoesNotPublishCoherenceEvent` (job status row stuck **Queued**). **Root cause:** dispatcher-queued **stale** “pending” progress could run **after** terminal **Finalize** and overwrite **`OperatorStatus`** back to **Queued**. **Fix:** in `TranscribeViewModel.CreateTranscriptApplyJobProgressReporter`, ignore non-terminal **Queued**/**Running** progress updates when the row is already **Succeeded** or **Failed** (`TranscribeViewModel.cs`).

## Proof

| Check | Result |
| ----- | ------ |
| `dotnet build src/VoiceStudio.App/VoiceStudio.App.csproj -c Debug -p:Platform=x64` | **0** errors |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build` | **3367** PASS / **278** skipped |
| `python scripts/ci/check_ibackendclient_creep.py` | **PASS** |
| `python scripts/check_empty_catches.py` | **PASS** |
| `.\scripts\verify.ps1 -Quick` | **PASS** — report `artifacts/verify/20260411_204602/verification_report.md` |
| `python scripts/run_verification.py` | **Overall PASS** — `.buildlogs/verification/last_run.json` (**completion_guard** PASS) |

## Umbrella

**GAP-067** remains **Open** (WCAG sweep, installer file assoc., taskbar progress, etc., per tracker).
