# GAP-008 Slice 16 — MainWindow **notification center** shell (bounded)

**Status:** Accepted (Tasks 409–417)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **Notification center Loaded wire + badge + flyout list binding + VM subscription lifecycle** — **`WireNotificationCenter`**, **`PropertyChanged`** subscription for unread badge, **`UpdateNotificationCenterBadge`**, **`CleanupNotificationCenter`** teardown; **not** file/jump-list activation, **not** Slice 12 HWND wiring, **not** startup/welcome **`Activated`**, **not** palette/catalog/toolbar/search bridges, **not** Loaded bootstrap/tail **orchestration** (hook assignment stays on **`MainWindow`** only).

## First seam (exact)

**`MainWindowNotificationCenterShellBridge`** owns:

- **`WireNotificationCenter()`** — resolve **`NotificationCenterViewModel`** via injected accessor; bind **`DataContext`** / **`ItemsSource`**; subscribe **`PropertyChanged`**; initial badge update (Loaded path only; ADR-047 / GAP-067).
- **`OnNotificationCenterViewModelPropertyChanged`** (private) — **`UnreadCount`** / **`HasUnread`** → **`UpdateNotificationCenterBadge`**.
- **`UpdateNotificationCenterBadge`** (private) — **`DispatcherQueue.TryEnqueue`** → **`UnreadBadge`** / **`UnreadBadgeText`** visibility and text.
- **`OnMarkAllReadClick`** / **`OnDismissItemClick`** — command forwards to VM (invoked from **`MainWindow`** XAML **`Click`** handlers as one-line forwards).
- **`CleanupNotificationCenter()`** — unsubscribe **`PropertyChanged`**, clear held VM reference; invoked from existing **`MainWindowLifetimeCleanupShellBridge`** channel lambda (**one line** on **`MainWindow`**); **implementation does not** move into **`MainWindowLifetimeCleanupShellBridge`** ([Slice 13 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE13_MAINWINDOW_LIFETIME_CLEANUP_SHELL.md)).

**`MainWindow`** removes private **`WireNotificationCenter`**, **`NotificationCenterViewModel_PropertyChanged`**, **`UpdateNotificationCenterBadge`**, and field **`_notificationCenterViewModel`**; keeps **`NotificationCenterMarkAllRead_Click`** / **`NotificationCenterDismissItem_Click`** as **forwards** to the bridge (XAML code-behind entry points). **`MainWindowShellLoadedBootstrap`** hook: **`WireNotificationCenter = () => _notificationCenterShellBridge.WireNotificationCenter()`**.

**Composition:** **`Func<NotificationCenterViewModel?>`** plus **`Func<>`** accessors for shell controls (**`Button`**, **`FrameworkElement`** flyout root, **`ListView`**, **`Border`** badge, **`TextBlock`** badge text) and **`DispatcherQueue`** — **no** **`AppServices`** / **`ServiceProvider`** inside the bridge.

## Relationship to Slice 13 (mandatory boundary)

| Layer | Responsibility |
| ----- | ---------------- |
| **`MainWindowLifetimeCleanupShellBridge`** | Invokes **`CleanupNotificationCenterViewModel`** channel **`Action`** only; **no** notification-center wiring logic inside Slice 13 types. |
| **Slice 16 bridge** | Owns **subscribe / unsubscribe / null** for **`NotificationCenterViewModel`** and badge updates. |

## Task 410 — Dependency / blast-radius map

| Responsibility | Current owner (`MainWindow`) | Target owner | Services / deps | Async / UI thread | Side effects | Coupling to other bridges | Regression risks |
| ---------------- | ---------------------------- | ------------ | --------------- | ----------------- | ------------ | ------------------------- | ------------------ |
| Wire VM to shell chrome | **`WireNotificationCenter`** | **`MainWindowNotificationCenterShellBridge`** | **`Func<NotificationCenterViewModel?>`**, named shell controls via **`Func<>`** | **`DispatcherQueue.TryEnqueue`** for badge | **`DataContext`**, **`ItemsSource`**, **`PropertyChanged`** | None — orthogonal to activation / jump-list / Slice 12 | Null VM → early return; wire twice without cleanup → duplicate handler (cleanup must run on close) |
| Badge refresh | **`NotificationCenterViewModel_PropertyChanged`**, **`UpdateNotificationCenterBadge`** | bridge (private) | VM **`INotifyPropertyChanged`** | Enqueued UI work | Visibility + text | None | Null controls after teardown → no-op if guarded |
| Mark all read / dismiss item | **`Click`** handlers | **`MainWindow`** forwards → **`OnMarkAllReadClick`**, **`OnDismissItemClick`** | VM **`IRelayCommand`** | Synchronous on UI thread | Service mutations via VM | None | **`DataContext`** type mismatch on dismiss button |
| Teardown | **`CleanupNotificationCenterViewModel`** lambda body | **`CleanupNotificationCenter`** on bridge; **`MainWindow`** one-line channel | Same VM instance as wire | N/A | Unsubscribe + null ref | Lifetime bridge **only** calls lambda | Missing cleanup → leak / duplicate **`PropertyChanged`** |

**Overlap with file activation / jump-list:** None — different DI resolution, different Loaded hooks, no shared static pending holder.

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`WireNotificationCenter`**, badge, VM **`PropertyChanged`**, **`CleanupNotificationCenter`**, command forwards | **IN** (this bridge) |
| **`MainWindowFileActivationShellBridge`**, **`MainWindowJumpListDispatchShellBridge`**, **`MainWindowJumpListTaskbarProgressShellBridge`** | **OUT** |
| Startup / welcome **`MainWindowStartupWelcomeActivationShellBridge`** | **OUT** (Slice 11) |
| Loaded bootstrap / tail **orchestration** | **OUT** (Slices 1 / 3) — **`MainWindow`** assigns hook delegates only |
| Tool catalog, palette, toolbar, search, navigation, workflow, recent mutation bridges | **OUT** |
| **`MainWindowLifetimeCleanupShellBridge`** implementation bodies | **OUT** — only channel **invocation** of **`CleanupNotificationCenter`** |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 417) |

## RHVoice (Task 417)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid that path string in **`MainWindowNotificationCenterShellBridge.cs`**.

## Not an extension bucket + bridge accretion (Task 416)

**`MainWindowNotificationCenterShellBridge`** is a **bounded seam owner** for **notification center shell glue** only. **Forbidden:** file activation, jump-list pending dispatch, Slice 12 **`WireJumpList`**, startup welcome, palette/catalog/toolbar/search, lifetime **implementation** beyond NC teardown.

**Distributed god object risk:** Each bridge remains a **single-story owner**. Routing unrelated shell features into this file recreates the monolith as **scatter**. Reject without a new brief + tests.

**Standing review (MAINWINDOW checklist 1–6):** Unchanged; spine extends only via [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**PR checklist:** Ran **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**; extended **only** **`tools/gap008_mainwindow_regression_filter.txt`**.

## Testing debt

WinUI **`Border`** / **`ListView`** full visual tree under MSTest host is **not** required for this slice; unit tests use **null-returning `Func<>`** for controls where UI is absent, **`NotificationCenterViewModel`** over **`INotificationCenterService`** Moq/fake for **wire → subscribe → `CleanupNotificationCenter` → unsubscribe** behavior. Badge enqueue with null controls: document as no-op path (no throw).

## Acceptance criteria

1. **`MainWindow`** does not embed **`WireNotificationCenter`** / badge / **`PropertyChanged`** bodies; Loaded hook delegates to **`MainWindowNotificationCenterShellBridge`**; lifetime channel delegates **`CleanupNotificationCenter`** to the bridge.
2. **`Gap008Slice16Tests`** + **`MainWindowNotificationCenterShellBridgeTests`**.
3. Regression spine strict superset of pre–Slice 16 count.

## Verification

**Canonical spine membership and count:** authoritative list is [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt); run [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1) and use script output **`Passed: N`** plus **`.buildlogs/gap008_spine/last_run_summary.json`** (Tasks **418–419**) — do not treat this brief as a rolling cumulative scoreboard for other slices.

**Why `N` matched (or mismatched) hand arithmetic:** see [GAP-008 MainWindow spine count reconciliation](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) (Tasks **418–419**): passing the **`#` documentation line** inside the same `--filter` string previously dropped the **`Gap008Slice5Tests`** clause (**118** vs **122** listed); the regression script now strips **`#`** lines so **`N` equals the OR-line `--list-tests` count**.

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice16Tests|FullyQualifiedName~MainWindowNotificationCenterShellBridgeTests"
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Result (2026-04-26):** `dotnet build` **0** errors; targeted Slice **16** tests **8** PASS; **`Run-Gap008MainWindowRegressionTests.ps1`** **Passed: 118**; `run_verification.py` **Overall: PASS** (record in **`.cursor/STATE.md`**). **Cumulative spine count** for slices after this land: run the script against the filter file — do not retrofit this brief when **N** changes.

## Changelog

- **2026-04-26:** Tasks **409–417** — Slice 16 chartered and landed; **`MainWindowNotificationCenterShellBridge`**; **`Gap008Slice16Tests`** + **`MainWindowNotificationCenterShellBridgeTests`**; filter superset; Slice **17+** planning only; spine-count authority note (Task 415).
