# Search Overlay Seam Scoping

**Status:** Scoped (2026-03-21)  
**Related:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md), [SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md](SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md), Architecture Wave Execution Plan

## Overview

The global search overlay (IDEA 5) allows users to search across panels and navigate to results. This doc scopes the seam extraction so MainWindow delegates orchestration instead of owning it.

---

## Current Owners

| Component | Responsibility |
|-----------|----------------|
| **MainWindow** | ShowGlobalSearch, HideGlobalSearch, GlobalSearchOverlay_Tapped; wires NavigateRequested; NavigateToSearchResultAsync, TrySelectItemInPanelAsync; FindNameOnContent for overlay/view/panel hosts |
| **GlobalSearchView** | UserControl; owns ViewModel, SearchBox, ResultsList; raises NavigateRequested on result selection; Show/Hide visibility |
| **GlobalSearchViewModel** | ISearchClient.SearchAsync; debounce; Results/FilteredResults; SelectedResult |
| **SearchClient** | Thin pass-through to IBackendClient.SearchAsync |

---

## Entry Points in MainWindow

| Entry Point | Location | Action |
|-------------|----------|--------|
| Ctrl+K shortcut | MainWindow.xaml.cs ~1223 | `ShowGlobalSearch()` |
| Menu: Tools > Global Search | MainWindow.Menu.cs ~89 | `ShowGlobalSearch()` |
| Overlay background tap | GlobalSearchOverlay_Tapped ~1752 | `HideGlobalSearch()` |
| NavigateRequested event | GlobalSearchView_NavigateRequested ~855 | `HideGlobalSearch()` + `NavigateToSearchResultAsync()` |
| Startup init | Constructor ~507 | Set GlobalSearchOverlay.Visibility = Collapsed |
| Loaded wiring | contentFE.Loaded ~441 | `globalSearchView.NavigateRequested += GlobalSearchView_NavigateRequested` |

---

## Collaborators and Dependencies

| Collaborator | Role |
|--------------|------|
| **IShellNavigationCoordinator** | ResolvePanelIdAlias, OpenPanelByIdAsync, GetPanelRegion |
| **PanelHost** (Left/Center/Right/Bottom) | Content = panel UserControl; obtained via FindNameOnContent |
| **INavigatablePanel** | NavigateToItemAsync(itemId, resultType, ct) — Library, Profiles, Timeline, ScriptEditor, Analyzer |
| **IToastNotificationService** | Error ("Panel Not Found", "Navigation Failed"); Success ("Navigation Complete") |
| **SearchNavigationContext** | FromSearchResult; ItemId, ResultType, Title, PanelId, CancellationToken |
| **SearchResultTypeMapper** | FromString, ToResultTypeString — maps backend types to SearchResultType enum |
| **FindNameOnContent** | Resolves "GlobalSearchView", "GlobalSearchOverlay", "LeftPanelHost", etc. |

---

## Event Flow

1. **Show:** User presses Ctrl+K or menu → ShowGlobalSearch → overlay Visibility.Visible, GlobalSearchView.Show() → focus SearchBox
2. **Search:** User types → GlobalSearchViewModel debounce → ISearchClient.SearchAsync → Results/FilteredResults updated
3. **Select:** User clicks result or Enter → NavigateToResult → Hide() → NavigateRequested(result)
4. **Navigate:** MainWindow.GlobalSearchView_NavigateRequested → HideGlobalSearch → NavigateToSearchResultAsync(result)
5. **Resolve:** SearchNavigationContext.FromSearchResult; ResolvePanelIdAlias(panelId) → canonicalId
6. **Open panel:** OpenPanelByIdAsync(canonicalId, region) → panel loads in PanelHost
7. **Select item:** targetHost.Content as UserControl; if INavigatablePanel → NavigateToItemAsync(itemId, resultType, ct)
8. **Feedback:** Toast success or error

---

## Navigation Targets

| PanelId (from backend) | Canonical ID | Region | INavigatablePanel |
|------------------------|--------------|--------|-------------------|
| library, profiles, timeline, script, analyzer | Resolved via ResolvePanelIdAlias | GetPanelRegion | LibraryView, ProfilesView, TimelineView, ScriptEditorView, AnalyzerView |

---

## Selection / Focus Responsibilities

| Component | Responsibility |
|-----------|----------------|
| **GlobalSearchView** | Focus SearchBox on Loaded; keyboard nav (Enter=go, Escape=hide, Up/Down=selection); Hide() before raising NavigateRequested |
| **MainWindow (current)** | Hide overlay; open panel; obtain panelView from PanelHost.Content; delegate to INavigatablePanel.NavigateToItemAsync |
| **INavigatablePanel** | Implement NavigateToItemAsync to select/focus item by ID |

---

## Error / No-Result Behavior

| Scenario | Current Behavior |
|----------|------------------|
| Empty panelId / unknown panel | Toast "Panel Not Found" |
| OpenPanelByIdAsync fails | Toast "Panel Not Found" |
| NavigateToSearchResultAsync throws | Toast "Navigation Failed" with exception message |
| Empty search results | GlobalSearchView shows EmptyStatePanel (ViewModel.TotalResults == 0) |
| Non-INavigatablePanel | TrySelectItemInPanelAsync no-op; success toast still shown |

---

## Test Surface

| Test Category | What to Prove |
|---------------|---------------|
| **Show/Hide** | Coordinator shows overlay; hides on Escape, overlay tap, or navigation |
| **Result → panel routing** | Given SearchResultItem(panelId, itemId, type), coordinator opens correct panel and calls NavigateToItemAsync |
| **Empty/bad panel ID** | Unknown panelId → error toast; no crash |
| **INavigatablePanel vs non-nav** | Nav panel: selection occurs; non-nav: no-op, success toast |
| **MainWindow delegation** | MainWindow wires coordinator; does not contain NavigateToSearchResultAsync / TrySelectItemInPanelAsync logic |

---

## Extraction Boundary (Proposed)

**Extract to:** `ISearchOverlayCoordinator` + `SearchOverlayCoordinator`

**Coordinator owns:**
- Show/Hide overlay (needs FindNameOnContent or equivalent resolver for GlobalSearchView, GlobalSearchOverlay)
- Subscribe to GlobalSearchView.NavigateRequested
- NavigateToSearchResultAsync logic (context, resolve, open panel, TrySelectItemInPanelAsync)
- Toast for errors/success

**MainWindow retains:**
- Wiring: shortcut → coordinator.Show(), menu → coordinator.Show()
- Overlay_Tapped → coordinator.Hide()
- Pass FindNameOnContent (or panel host resolver) to coordinator
- Loaded: wire NavigateRequested to coordinator

**Out of scope for this seam:**
- GlobalSearchViewModel / SearchClient (already separated)
- Command palette (separate overlay)
- Tool catalog (separate)

---

## Changelog

- 2026-03-21: Initial scoping. Owners, entry points, collaborators, event flow, test surface, extraction boundary defined.
