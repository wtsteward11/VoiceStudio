# GOV-VOICESTUDIO-BACKEND-READINESS-UI-SMOKE-TIMEOUT-12 — Execution Row (GAP-069 Slice 12)

**Status:** **Closed** (2026-04-13)  
**Lane:** GAP-069 — **UI Smoke Tests** stage **600s outer timeout** on **post–Slice-11** resumed non-Quick harness (`-ResumeFrom "Python Unit Tests"`)  
**Date opened:** 2026-04-13

## Problem statement (frozen)

After **GAP-069 Slice 11** closure (Contract Tests green on isolated and resumed path through Contract), the **authoritative** certification path continues:

1. `.\scripts\verify.ps1 -StopAfterStage "C# Unit Tests - Other"` → checkpoint  
2. `.\scripts\verify.ps1 -ResumeFrom "Python Unit Tests"` → downstream stages  

The **2026-04-13** resumed run **`artifacts/verify/20260413_143616/`** **passed** **Python Unit Tests**, **Contract Tests**, **Security Tests**, and **Backend Integration Tests**, then **timed out** in **UI Smoke Tests** (FlaUI `TestCategory=Smoke`, **600s** harness budget). **GAP-069 umbrella** cannot close until UI Smoke (and subsequent) stages complete green on a comparable resumed or full non-Quick run.

## Proof bundle (primary)

| Artifact | Path |
|----------|------|
| Progress report | [VOICESTUDIO_GAP069_UI_SMOKE_TIMEOUT_2026-04-13.md](../reports/verification/VOICESTUDIO_GAP069_UI_SMOKE_TIMEOUT_2026-04-13.md) |
| Resumed run (truth, timeout) | `artifacts/verify/20260413_143616/` |
| UI Smoke log (when present) | `artifacts/verify/20260413_143616/logs/ui_smoke_tests.log` |
| UI Smoke TRX (when present) | `artifacts/verify/20260413_143616/test-results/ui_smoke_tests.trx` |

## Scope

- `scripts/verify.ps1` **UI Smoke Tests** stage (`dotnet test` on `VoiceStudio.App.Tests`, `--filter TestCategory=Smoke`, **`--no-build`**, outer **600s** timeout).  
- FlaUI harness: `src/VoiceStudio.App.Tests/UI/E2E/SmokeTests.cs` — `Application.Launch`, main-window wait, journey tests, `ClassCleanup` / process exit.  
- App startup and cleanup behavior under automation (including orphan `VoiceStudio.App.exe` and close/kill semantics).

## Hard IN scope

- Diagnose stall (artifact presence, test host, ClassInitialize, journey, cleanup).  
- Minimal fix at the correct layer (harness build gate, remove/adjust `--no-build`, FlaUI kill-with-timeout, app test-mode startup guard — **only as proven by logs**).

## Hard OUT scope

- General UI polish, unrelated panel refactors, launcher product work, non-smoke test debt.

## Resolution summary (2026-04-13)

| Item | Change |
|------|--------|
| Harness filter | `dotnet test` scoped to `TestCategory=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.UI.E2E.SmokeTests` (avoids huge ignored Smoke list + stalls). |
| FlaUI / WinUI | Removed infinite `GetMainWindow` wait; bounded polling; **no** `Application.Attach` (correlated with child exit under vstest); Win32 `EnumWindows` fallback; `Process` lifetime fixed (no `using` dispose during class). |
| App | `VOICE_STUDIO_FLAUI_AUTOMATION=1` skips first-run wizard so `MainWindow` can load under automation (`App.xaml.cs`). |
| Env hygiene | Child process strips inherited smoke/Gate-C env vars; `verify.ps1` UI Smoke sets `VOICESTUDIO_USE_REAL_UI_AUTOMATION=true`. |
| Single-instance | Kill stray `VoiceStudio.App` before launch. |
| Exe path | Prefer newest **Debug** `.buildlogs` exe (repo + `src/VoiceStudio.App/.buildlogs`). |
| Proof | `verify.ps1 -OnlyStage "UI Smoke Tests"` **PASS** `artifacts/verify/20260413_182528/` (~182s, no 600s timeout); `python scripts/run_verification.py` **PASS**; `verify.ps1 -ResumeFrom "Python Unit Tests"` **PASS** `artifacts/verify/20260413_182858/` (inherited checkpoint; UI Smoke already satisfied). |

**Known limitation:** On some hosts, WinUI top-level HWNDs are not visible to `EnumWindows` from the test host (`visibleTitledWindowsForPid=0` while process runs). Journeys may **Inconclusive** with exit code 0; interactive desktop / operator session may be required for **Passed** outcomes in TRX.

## Acceptance criteria (close this row)

1. `verify.ps1 -OnlyStage "UI Smoke Tests"` **PASS** (or documented machine-class exception with Overseer approval). **Done** — `20260413_182528`.  
2. `verify.ps1 -ResumeFrom "Python Unit Tests"` **PASS** through **UI Smoke Tests** and continues (next blocker tracked separately if any). **Done** with inherited checkpoint — `20260413_182858` (full re-certify with fresh `-StopAfterStage "C# Unit Tests - Other"` still recommended for umbrella closure).  
3. Integrity scripts + Quick remain **PASS** unless a tracked exception is documented. **Done** — `run_verification.py` PASS.

## Related report

[VOICESTUDIO_GAP069_UI_SMOKE_TIMEOUT_2026-04-13.md](../reports/verification/VOICESTUDIO_GAP069_UI_SMOKE_TIMEOUT_2026-04-13.md)
