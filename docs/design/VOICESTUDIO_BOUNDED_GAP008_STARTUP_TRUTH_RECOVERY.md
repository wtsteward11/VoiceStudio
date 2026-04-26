# GAP-008 — Startup truth recovery (bounded hotfix)

**Status:** **Closed** — process launch (§A), structural pins (§B), operator visual proof (§C), and honest coverage limits (§D).  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); **Slice 11** welcome/activation is a separate bounded brief (prerequisite: this lane is complete per §C).

## Problem

Shell can show **stuck “Starting…”** in the status bar and **inconsistent** indicators (e.g. default green dots from XAML while copy never leaves startup) when `IStartupStateService` reaches **`BackendReady`** / **`Degraded`** but **`StatusBarCoordinator`** does not re-run activity-driven UI refresh.

## First seam (exact)

**`StatusBarCoordinator`** owns subscription to **`IStartupStateService.StateChanged`** and, on any startup state change, enqueues a full **`UpdateActivityIndicators`** refresh so **`StatusText`**, **engine**, and **processing** indicators align with **`IsReady`** and **`ErrorPresentationService.IsBackendOffline`**.

**Reachability → status copy:** **`OnBackendReachabilityChanged`** no longer assigns **`StatusText`** directly; it merges reachability into **`ActivityStatusChangedEventArgs`** and calls **`UpdateActivityIndicators`** so **`ApplyPrimaryStatusText`** is the single owner of primary status line copy (offline / starting / ready / processing).

**Types:** [`StatusBarCoordinator`](../../src/VoiceStudio.App/Services/StatusBarCoordinator.cs); [`GlobalTransportControl`](../../src/VoiceStudio.App/Controls/GlobalTransportControl.xaml.cs) (reference pattern only — already subscribes to **`StateChanged`**).

## In scope

| Item | Notes |
| ---- | ----- |
| **`StatusBarCoordinator.Subscribe` / `Unsubscribe`** | Register / detach **`IStartupStateService.StateChanged`**; catch-up refresh if **`IsReady`** already true when subscribing |
| **`OnBackendReachabilityChanged`** | Drive **`UpdateActivityIndicators`** with merged **`NetworkStatus`**; no duplicate hard-coded **"Ready"** |
| **`ApplyPrimaryStatusText`** | Centralize **`StatusText`** for offline, not-ready, and idle/processing states |
| **`Gap008StartupTruthTests`** | File pins: coordinator subscribes and unsubscribes startup; transport control keeps **`StateChanged`** → **`Refresh`** |

## Explicitly NOT this lane

| Cluster | Notes |
| ------- | ----- |
| Slice 11 welcome / **`MainWindow_Activated`** extraction | Own brief — not part of this coordinator-only lane |
| Tool catalog, palette, recents, toolbar routing, RHVoice | Unchanged |
| **`ErrorPresentationService.OnBackendConnected`** first-pulse semantics | **Documented:** reachability **`true`** on first connect while **`!IsReady`** is intentionally suppressed; coordinator **does not** depend on that pulse for final ready paint after **`SetBackendReady`** |

## Static analysis evidence (pre-fix, 2026-04-25)

| Question | Finding |
| -------- | ------- |
| Can **`IsReady`** be true while **`StatusText`** stays **Starting…**? | **Yes:** **`SetBackendReady`** fires **`StateChanged`** and **`GlobalTransportControl`** refreshes, but **`StatusBarCoordinator`** only ran **`UpdateStatusText`** from **`ActivityStatusChanged`** — if no further activity event, the last forced **Starting…** never clears. |
| Green dots + **Starting…** | **`MainWindow.xaml`** defaults **`NetworkIndicator`** / **`EngineIndicator`** to success brushes until **`UpdateEngineIndicator`** runs; stalled coordinator refresh leaves defaults visible alongside stale copy. |

## Repro / evidence template (operator)

Use when validating a regression or CI gap:

1. **Launch:** Debug x64, default backend auto-start (or document external backend).
2. **Observe:** Bottom **`StatusBar_StatusText`**, **`GlobalTransportControl`** titles, startup overlay visibility vs shell.
3. **At failure time capture:** `StartupStateService.CurrentState`, **`ErrorPresentationService.IsBackendOffline`**, **`GET /health`** result if backend port known.
4. **Logs:** Excerpt around **`OnBackendStarted`** / **`StartBackendHealthMonitoring`** (Debug or structured log).
5. **Artifact:** Screenshot under `%LOCALAPPDATA%\VoiceStudio\crashes\` or attach to task brief.
6. **Answer:** Is **`IsReady`** true while **`StatusText`** still **"Starting…"**? (pre-fix: yes when no activity event followed **`SetBackendReady`**).

## Verification

**GAP-008 MainWindow regression spine:** extend tokens only in [`tools/gap008_mainwindow_regression_filter.txt`](../tools/gap008_mainwindow_regression_filter.txt); run:

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Manual:** Cold launch — overlay hides when backend ready; **`StatusText`** shows **Ready** (or processing line) when idle; transport strip not stuck on **Starting…** when **`IsReady`**.

## Operator proof (cold launch)

**Recorded:** 2026-04-25  
**Build:** Debug, x64 — executable: `src\VoiceStudio.App\bin\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe`  
**Canonical operator visual record:** [PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md](../reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md)

### A — Process cold start (automation / agent)

| Step | What was done | Result (2026-04-25) |
| ---- | --------------- | ------------------- |
| Prior instance | `Stop-Process -Name VoiceStudio.App -Force` (single-instance mutex would otherwise exit the second process immediately) | Cleared or none |
| Launch | `Start-Process` of the **Debug x64** `VoiceStudio.App.exe` with working directory = output folder | **RUNNING** after **12s** watchdog (PID at run time varies) |

**Note:** Process survival supports release hygiene; **human-visible** startup truth is recorded in **§C** via the linked proof document.

### B — Automated structural pins (CI / local `dotnet test`)

| Check | Evidence | Result |
| ----- | -------- | ------ |
| Primary status line logic not stuck on reachability-only luck | [`Gap008StartupTruthTests`](../../src/VoiceStudio.App.Tests/Views/Gap008StartupTruthTests.cs); retention pins for **`StateChanged`** + subscribe-time catch-up | PASS |
| Reachability does not assign **`StatusText`** directly | `Gap008StartupTruthTests.StatusBarCoordinator_OnBackendReachabilityChanged_DoesNotAssignStatusTextDirectly` | PASS |
| Transport strip pattern preserved | `GlobalTransportControl` **`StateChanged`** → **`Refresh`** pin | PASS |
| Widened GAP-008 regression spine | `dotnet test` filter in **Verification** above | See **Truth Sync** / last green run |

### C — Visual confirmation (operator — Task 359 bullets) — **PASS**

Recorded in [PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md](../reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md) for the **2026-04-25** cold-launch session:

1. Bottom **`StatusBar_StatusText`** shows **Ready** (not stuck on **Starting…**) after backend steady state.  
2. Top shell / transport — **no** stale **Starting…** pollution in the observed session.  
3. **StartupOverlay** absent — workspace usable.  
4. Primary status and shell — consistent steady state (e.g. **Job: Idle**, interactive window).

### D — Coverage limits (do not over-claim)

The proof in §C is **one** cold path / **one** session. It does **not** prove: repeated relaunch-only matrices, degraded-backend-only recovery, restart-after-failure, or suspend/resume. Extend coverage with additional briefs or operator sessions when those lanes matter.

### Anti-pattern guardrail (Task 367 alignment)

The **`IStartupStateService.StateChanged` → `UpdateActivityIndicators`** wiring is **status-bar primary-line truth only**. Do **not** reuse it as a generic “refresh the shell” pattern for unrelated features.

## RHVoice

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice remains frozen.
