# GAP-070 — Shell / user-preference persistence authority lane closure

**Lane:** `GOV_VOICESTUDIO_GAP070_USER_PREFERENCES_PERSISTENCE_BOUNDARY_01`  
**Tracker:** GAP-070 **Closed** — single merge-save pipeline for workspace settings; `ActivePanelId` aligned on `SavePanelState`; deterministic region restore order (Left → Center → Right → Bottom → Floating); GAP-014 DI relapse guards retained  
**Date:** 2026-04-07

## 1. Scope delivered

- **`PanelStateService`:** `_workspaceSettingsSaveGate` serializes `SaveCurrentWorkspaceAsync` (load-merge-save cannot interleave). `SavePanelState` sets `regionState.ActivePanelId` when the region already exists (PanelHost persist path).
- **`MainWindow.Workspaces`:** `RestorePanelsFromLayoutAsync` orders regions by `PanelRegion` before `LoadPanelAsync`; `RestoreSplitterRatios` after content (source markers `GAP-070-order-1` / `GAP-070-order-2`). XML summary shortened to satisfy `test_mainwindow_total_budget` (combined `MainWindow*.cs` ≤ 200_000 bytes).
- **Tests:** `ShellPersistenceAuthoritySeamTests` (**4**); `WorkspaceAuthoritySeamTests` (**4**, GAP-014 + GAP-070 narrative); `PanelStateServiceTests.SavePanelState_UpdatesActivePanelId_WhenRegionExists` (**1**).

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings only) |
| Targeted MSTest (GAP-070 slice) | `dotnet test ... --filter "FullyQualifiedName~ShellPersistenceAuthoritySeamTests\|FullyQualifiedName~WorkspaceAuthoritySeamTests\|FullyQualifiedName~PanelStateServiceTests.SavePanelState_UpdatesActivePanelId_WhenRegionExists"` | **9** PASS |
| GAP-013 regression | `dotnet test ... --filter "FullyQualifiedName~PanelHost"` | **7** PASS |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS (**2** deselected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260407_005255/` |
| Ledger / guard | `python scripts/run_verification.py` (post-commit) | PASS — `.buildlogs/verification/last_run.json` `timestamp_short` **20260407-010128** (**completion_guard** PASS) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260407_005255/`
- Verification JSON: `.buildlogs/verification/last_run.json` — **`timestamp_short` `20260407-010128`** (mirrored in `.cursor/STATE.md` **Latest verify artifact**).

## 4. Rollback

Revert the GAP-070 scoped commit(s). Restores prior `SavePanelState`, `RestorePanelsFromLayoutAsync`, and `SaveCurrentWorkspaceAsync` behavior if required.
