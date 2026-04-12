# GOV-VOICESTUDIO-GAP067-FILE-ACTIVATION-04 — Execution Row

**Status:** CLOSED (2026-04-12 — proof in [VOICESTUDIO_GAP067_FILE_ACTIVATION_LANE_CLOSURE_2026-04-12.md](../reports/verification/VOICESTUDIO_GAP067_FILE_ACTIVATION_LANE_CLOSURE_2026-04-12.md))

**Lane:** GAP-067 slice 4 — File activation authority (`.voiceproj`, `.vstudio`, `.vprofile`)

## Acceptance contract

- [x] `.vstudio` registered in Inno Setup and WiX (HKCR, `VoiceStudio.Collaboration` progid)
- [x] `FileActivation` + `FileActivationArgs` parse bare argv for recognized extensions
- [x] `JumpListActivation.HasPending()` gates `FileActivation` so jump list wins
- [x] `.voiceproj` opens via `OpenProjectByPathAsync` → `IProjectRepository.OpenProjectFileAsync` → timeline selection
- [x] `.vstudio` degrades: info toast + `OpenProjectAsync()` (picker)
- [x] `.vprofile` degrades: info toast + Profiles panel navigation
- [x] Startup `IsReady` deferral matches jump-list dispatch
- [x] Tests: `Gap067Slice4Tests` ≥9, `FileActivationSeamTests` ≥6
- [x] Verification: build, full App.Tests, creep, empty-catch, `verify.ps1 -Quick`, `run_verification.py`

## Hard OUT

- Backend collaboration HTTP routes; profile import API
- WCAG, notification center, jump list edits, taskbar progress edits
- Generic file-association plugin framework

## Closeout

| Proof | Path |
| ----- | ---- |
| Closure | [VOICESTUDIO_GAP067_FILE_ACTIVATION_LANE_CLOSURE_2026-04-12.md](../reports/verification/VOICESTUDIO_GAP067_FILE_ACTIVATION_LANE_CLOSURE_2026-04-12.md) |
| Quick verify | `artifacts/verify/20260411_224656/` |
| Rolling verifier | `.buildlogs/verification/last_run.json` |
