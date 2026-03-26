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
| Event subscription | MainWindow Loaded | NavigateRequested += handler |
| Shortcut/menu wiring | MainWindow | Ctrl+K, Tools > Global Search |
| Overlay tap handler | MainWindow.GlobalSearchOverlay_Tapped | Thin event routing; delegates coordinator.Hide() |
| Startup Visibility init | MainWindow constructor | GlobalSearchOverlay.Visibility = Collapsed |

---

## Changelog

- 2026-03-21: Initial ownership contract per Architecture Wave Next Slice Plan Task 1C.
