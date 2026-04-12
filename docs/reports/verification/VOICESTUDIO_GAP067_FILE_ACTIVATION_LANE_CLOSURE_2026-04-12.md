# VOICESTUDIO — GAP-067 File Activation (Slice 4) — Lane Closure

**Date:** 2026-04-12  
**Lane:** GOV-VOICESTUDIO-GAP067-FILE-ACTIVATION-04  
**Execution row:** [GOV_VOICESTUDIO_GAP067_FILE_ACTIVATION_04_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP067_FILE_ACTIVATION_04_EXECUTION_ROW.md) — **Closed**

## Summary

Bounded slice 4 delivers **shell file activation** for the unpackaged app: **`FileActivation`** + **`FileActivationArgs`** parse bare `argv` from Windows file associations; **`JumpListActivation.HasPending()`** ensures jump list wins; **`MainWindow`** dispatches after jump list with **`IStartupStateService`** deferral. **`.voiceproj`** opens via **`IProjectWorkflowCoordinator.OpenProjectByPathAsync`** → **`TimelineProjectHandlers.OpenProjectByPathAsync`** → **`IProjectRepository.OpenProjectFileAsync`**. **`.vstudio`** and **`.vprofile`** use **honest degradation** (info toast + open dialog / Profiles navigation). **`.vstudio`** is registered in **Inno Setup** and **WiX** (`VoiceStudio.Collaboration`). Tests: **`Gap067Slice4Tests`** **14** + **`FileActivationSeamTests`** **6**.

## Proof

| Check | Result |
| ----- | ------ |
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3405** PASS / **278** skipped |
| `python scripts/ci/check_ibackendclient_creep.py` | **PASS** |
| `python scripts/check_empty_catches.py` | **PASS** |
| `.\scripts\verify.ps1 -Quick` | **PASS** — report `artifacts/verify/20260411_224656/verification_report.md` |
| `python scripts/run_verification.py` | **Overall PASS** — `.buildlogs/verification/last_run.json` (**completion_guard** PASS) |

## Umbrella

**GAP-067** remains **Open** (WCAG sweep, progressive disclosure, cold-start stretch, etc., per tracker). Installer **`.vstudio`** association + in-app **`FileActivation`** path for this slice are **Closed**.
