# GAP-007 — PanelHost ContentProperty boundary lane closure

**Lane:** `GOV_VOICESTUDIO_GAP007_PANELHOST_CONTENTPROPERTY_BOUNDARY_01`  
**Tracker:** GAP-007 **Closed** (shell / XAML seam — no `ContentProperty` shadow on `PanelHost`)  
**Date:** 2026-04-07

## 1. Scope delivered

- `PanelHost`: `HostedPanel` / `HostedPanelProperty` (`UIElement`) replace shadowed `Content` / `ContentProperty`; `OnContentChanged` + LRU/cache/offline overlay logic unchanged except symbol renames.
- `PanelHost.xaml`: `ContentPresenter` binds `Content="{x:Bind HostedPanel, Mode=OneWay}"`.
- Consumers updated: `MainWindow.xaml.cs`, `MainWindow.Workspaces.cs`, `MainWindow.Smoke.cs`, `SearchOverlayCoordinator.cs`.
- Proof: `PanelHostSeamTests` (reflection — no declared `ContentProperty`; `HostedPanelProperty` registered; CLR `HostedPanel` type `UIElement`).

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable warnings only) |
| Targeted MSTest | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~PanelHostSeamTests"` | **3** PASS |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS (**2** deselected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_211930/` |
| Ledger / guard | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **20260406-212656** (**completion_guard** PASS; post-commit) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260406_211930/`
- Verification JSON: `.buildlogs/verification/last_run.json` (`timestamp_short`: **20260406-212656** terminal; **20260406-212438** pre-commit closure matrix)

## 4. Rollback

Revert the GAP-007 commit(s). Restores prior `Content`/`ContentProperty` names on `PanelHost` and callers.
