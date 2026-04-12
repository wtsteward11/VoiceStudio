# GOV-VOICESTUDIO-GAP067-JUMP-LISTS-02 — Execution Row

**Lane ID:** GOV-VOICESTUDIO-GAP067-JUMP-LISTS-02  
**Umbrella:** GAP-067 (bounded slice 2 — jump lists only)  
**Status:** **CLOSED** (2026-04-12) — [closure](../reports/verification/VOICESTUDIO_GAP067_JUMP_LISTS_LANE_CLOSURE_2026-04-12.md)  
**Scope type:** Shell integration (Win32 taskbar jump list) on unpackaged app; **no** WCAG sweep; **no** notification-center changes; **no** panel registration  

## Hard IN

- One canonical **`JumpListService`** projecting **`RecentProjectsService.AllProjects`** (no second recency store)
- Win32 **`ICustomDestinationList`** / unpackaged-safe COM path (not `Windows.UI.StartScreen.JumpList` MSIX-only API)
- Static tasks: **New Project**, **Open Project** (`--jumplist-new`, `--jumplist-open-dialog`)
- Recent items: `--jumplist-open "{path}"` from canonical recents (cap **10**)
- Activation handling in **`App`** + dispatch from **`MainWindow`** when **`IStartupStateService.IsReady`**
- Tests: `Gap067Slice2Tests` + `JumpListServiceSeamTests`
- Governance closure in same wave

## Hard OUT

- WCAG sweep / full a11y umbrella
- Notification center / title bar redesign
- Workspace or panel architecture refactor
- MSIX-only JumpList APIs without unpackaged fallback
- Additional static tasks beyond the two listed (future slice)

## Acceptance contract

- [x] `JumpListService` singleton registered in DI (`AppServices`)
- [x] Static tasks use `--jumplist-new` and `--jumplist-open-dialog`; recent uses `--jumplist-open`
- [x] Recent items projected from `RecentProjectsService` (no duplicate store)
- [x] Jump list refresh on shell load + debounced refresh on recents `PropertyChanged`
- [x] `JumpListActivation` parses `LaunchActivatedEventArgs` / command line; pending consumed once after startup ready
- [x] `Gap067Slice2Tests` source-contract tests **≥ 9** PASS (**11**)
- [x] `JumpListServiceSeamTests` **≥ 6** PASS (**6**)
- [x] Full `VoiceStudio.App.Tests` PASS (no regression)
- [x] `python scripts/ci/check_ibackendclient_creep.py` PASS
- [x] `python scripts/check_empty_catches.py` PASS
- [x] `.\scripts\verify.ps1 -Quick` PASS
- [x] `python scripts/run_verification.py` → Overall PASS; `completion_guard` PASS
- [x] Execution row CLOSED; closure report; tracker; registry; STATE; openmemory

## Proof (fill on close)

- App.Tests: **3367** PASS / **278** skipped  
- Quick artifact: `artifacts/verify/20260411_204602/`  
- Closure report: [VOICESTUDIO_GAP067_JUMP_LISTS_LANE_CLOSURE_2026-04-12.md](../reports/verification/VOICESTUDIO_GAP067_JUMP_LISTS_LANE_CLOSURE_2026-04-12.md)
