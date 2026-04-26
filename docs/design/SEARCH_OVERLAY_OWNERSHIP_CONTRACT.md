# Search Overlay Ownership Contract

**Purpose:** Crisp contract so the search coordinator does not become the next blob.  
**Date:** 2026-03-21  
**Related:** [SEARCH_OVERLAY_SCOPING.md](SEARCH_OVERLAY_SCOPING.md), [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

---

## Coordinator Owns

| Responsibility | Notes |
|----------------|-------|
| Show/Hide overlay | Visibility of GlobalSearchOverlay, GlobalSearchView.Show/Hide |
| NavigateToSearchResultAsync | Context creation, panel resolution, open panel, TrySelectItemInPanelAsync |
| Panel routing | ResolvePanelIdAlias, OpenPanelByIdAsync, GetPanelRegion |
| TrySelectItemInPanelAsync | INavigatablePanel.NavigateToItemAsync when panel supports it |
| Toast for errors/success | Panel Not Found, Navigation Failed, Navigation Complete |

---

## Coordinator Does Not Own

| Responsibility | Owner |
|----------------|-------|
| Search execution | GlobalSearchViewModel + ISearchClient |
| Panel content | Individual panels (Library, Profiles, Timeline, etc.) |
| FindNameOnContent implementation | Injected; caller provides resolver |

---

## Services Coordinator May Call

| Service | Purpose |
|---------|---------|
| IShellNavigationCoordinator | ResolvePanelIdAlias, OpenPanelByIdAsync, GetPanelRegion |
| IToastNotificationService | Optional; errors and success feedback |
| FindNameOnContent (injected) | Resolve GlobalSearchView, GlobalSearchOverlay, LeftPanelHost, etc. |

---

## Must Remain in Shell/UI

| Responsibility | Location | Reason |
|----------------|----------|--------|
| Event subscription | MainWindow ctor | `NavigateRequested +=` on `GlobalSearchView` (handler forwards to bridge) |
| Shortcut/menu wiring | MainWindow | Ctrl+K, Tools > Global Search → thin forward |
| Overlay tap handler | MainWindow.GlobalSearchOverlay_Tapped | XAML entry point; thin forward to **`MainWindowSearchOverlayShellBridge.OnOverlayTappedForDismiss`** → coordinator **`Hide()`** when tap is on overlay root |
| Startup overlay collapsed | MainWindow ctor | **`MainWindowSearchOverlayShellBridge.EnsureGlobalSearchOverlayCollapsed()`** → **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** (same **`FindName`** + **`Collapsed`** semantics; non-**`FrameworkElement`** find is a no-op) |

**GAP-008 Slice 6:** Thin routing for show / navigate / dismiss / startup collapse lives in **`MainWindowSearchOverlayShellBridge`**; **`SearchOverlayCoordinator`** remains the implementation of **`ISearchOverlayCoordinator`**.

---

## Changelog

- 2026-04-24: Slice 6 — document **`MainWindowSearchOverlayShellBridge`** as shell thin-routing owner; subscription and XAML handler names stay on **`MainWindow`**.
- 2026-03-21: Initial ownership contract per Architecture Wave Next Slice Plan Task 1C.
