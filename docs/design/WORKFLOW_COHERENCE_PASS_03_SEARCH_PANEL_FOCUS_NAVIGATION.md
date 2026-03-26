# Workflow Coherence Pass 03 — Search → Panel Focus → Item Navigation

**Purpose:** Bounded product-facing pass for global search → target panel activation → selection/follow of the result item.  
**Date:** 2026-03-24  
**Status:** **Complete** (2026-03-24).  
**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md), [WORKFLOW_COHERENCE_PASS_02_PROJECT_TIMELINE_EFFECTS_MIXER.md](WORKFLOW_COHERENCE_PASS_02_PROJECT_TIMELINE_EFFECTS_MIXER.md), [WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md](WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md)

**Authoritative prior proof:** Pass 02 closed with `artifacts/verify/20260324_012252` (see [WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md](WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md)).

**This pass proof:** See §11 — run `verify.ps1 -Quick` and record the folder named in `artifacts/verify/latest_pointer.json` after execution.

---

## 1. Participating components (as implemented)

| Component | Role |
|-----------|------|
| [GlobalSearchView.xaml.cs](../../src/VoiceStudio.App/Views/GlobalSearchView.xaml.cs) | Result click / Enter → `NavigateRequested` event |
| [GlobalSearchViewModel.cs](../../src/VoiceStudio.App/ViewModels/GlobalSearchViewModel.cs) | Calls `ISearchClient.SearchAsync` → `SearchResultItem` list |
| [SearchClient.cs](../../src/VoiceStudio.App/Services/SearchClient.cs) | HTTP `GET /api/search` |
| [search.py](../../backend/api/routes/search.py) | Builds results: `profile`→`profiles`, `project`→`timeline`, `audio`→`library`, `marker`→`timeline`+metadata, `script`→`script_editor` |
| [MainWindow.xaml.cs](../../src/VoiceStudio.App/MainWindow.xaml.cs) | Subscribes `NavigateRequested` → `ISearchOverlayCoordinator.HandleNavigateRequestedAsync` |
| [SearchOverlayCoordinator.cs](../../src/VoiceStudio.App/Services/SearchOverlayCoordinator.cs) | Resolve panel alias, `OpenPanelByIdAsync`, `PanelHost.Content`, `INavigatablePanel.NavigateToItemAsync`, toasts |
| [SearchResultTypeMapper.cs](../../src/VoiceStudio.Core/Panels/SearchResultTypeMapper.cs) | `TryMapToPanelId`, `ToPanelResultTypeString` (Core — not under `VoiceStudio.App/Services`) |
| [ShellNavigationCoordinator.cs](../../src/VoiceStudio.App/Services/ShellNavigationCoordinator.cs) | `ResolvePanelIdAlias`, `GetPanelRegion`, `OpenPanelByIdAsync` (startup gate) |
| [PanelHost](../../src/VoiceStudio.App/Controls/PanelHost.xaml.cs) | `Content` = loaded panel `FrameworkElement` |
| `INavigatablePanel` | [LibraryView, ProfilesView, TimelineView, AnalyzerView, ScriptEditorView](../../src/VoiceStudio.App/Views/Panels/) |

`ISelectionBroadcastService` / `IContextManager` are **not** on the search navigation hot path for this pass (search uses coordinator → panel directly).

---

## 2. Target behavior (achieved in scope)

1. **Search result** supplies stable `Id`, `Type`, `PanelId`, `Title`, optional `Metadata`.
2. **Panel activation** via shell coordinator opens the canonical panel in the correct region.
3. **Selection** via `INavigatablePanel.NavigateToItemAsync` with canonical or passthrough type string + optional `searchMetadata`.
4. **Failure clarity:** distinct toasts for panel not found, open failed, host missing, content not ready, panel not navigable, selection incomplete, vs full success.
5. **Partial success:** panel opens but item not selected → warning toast, not success.

---

## 3. Change matrix (final)

| Change ID | Target behavior | Primary owner | Supporting | Result types | Tests | Proof |
|-----------|-----------------|---------------|------------|--------------|-------|-------|
| C1 | Single type string for panels: known API types canonical; unknown types pass through lowercased | `SearchResultTypeMapper.ToPanelResultTypeString` | `SearchOverlayCoordinator` | All | Coordinator + metadata test | dotnet test |
| C2 | Resolve panel content via `PanelHost` or test hook | `SearchOverlayCoordinator` | `PanelHost`, `PanelNavigationTestHook` (tests only) | — | Coordinator | dotnet test |
| C3 | Honor `NavigateToItemAsync` bool; no false success toast | `SearchOverlayCoordinator` | Panels | All navigable | Coordinator | dotnet test |
| C4 | Best-effort `Focus(FocusState.Programmatic)` when a `FrameworkElement` is available | `SearchOverlayCoordinator` | — | — | Manual optional | — |
| C5 | Workflow-step toasts: Error / Warning / Info / Success | `SearchOverlayCoordinator` | `IToastNotificationService` | — | Coordinator | dotnet test |
| C6 | Marker: navigate to project when `metadata.project_id` present | `TimelineView.NavigateToItemAsync` | Search API metadata | `marker` | Coordinator metadata test | dotnet test |

---

## 4. Out-of-scope (strict)

- No search indexing/relevance/ranking changes.
- No broad shell rewrite (only coordinator + existing panel/stubs).
- No BackendClient / transport extraction.
- No script editor → synthesis, record → transcribe, or backup/restore flows in this pass.
- No visual/theming/copy deck overhaul except toast strings for navigation steps.
- No merging Pass 04+ into this pass.

---

## 5. Tests / proof required

- **Unit:** `SearchOverlayCoordinatorTests` (routing, host missing, non-navigable, selection true/false, empty id, metadata forwarded).
- **Build:** `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- **verify.ps1 -Quick:** authoritative folder = contents of `artifacts/verify/latest_pointer.json` after run.

---

## 9. As-Is Workflow Map (baseline)

1. **Results origin:** User types in `GlobalSearchView` → debounced `GlobalSearchViewModel.SearchAsync` → `SearchClient.SearchAsync` → JSON `SearchResponse` → `SearchResultItem` in `FilteredResults`.
2. **Navigate trigger:** `ResultsList_ItemClick` or Enter → `NavigateToResult` → hides view → `NavigateRequested` → `MainWindow.GlobalSearchView_NavigateRequested` → `HandleNavigateRequestedAsync` (coordinator hides overlay again).
3. **Panel choice:** `result.PanelId` lowercased → `ResolvePanelIdAlias` (e.g. `timeline`→`Timeline`, `script_editor`→`ScriptEditor`).
4. **Region:** `GetPanelRegion(canonicalId)` → `OpenPanelByIdAsync(canonicalId, region)`.
5. **Content:** `FindName("{Region}PanelHost")` as `PanelHost` → `Content` as `FrameworkElement` → `INavigatablePanel` when implemented.
6. **Selection:** `SearchResultTypeMapper.ToPanelResultTypeString(Type)` → `NavigateToItemAsync(id, type, ct, metadata)`.
7. **Per-panel handling:**
   - **Profiles:** `profile` only → `NavigateToProfileAsync`.
   - **Library:** `audio`, `project_audio` → `NavigateToAssetAsync`.
   - **Analyzer:** `audio`, `project_audio` → `NavigateToAudioAsync`.
   - **Timeline:** `project` → project id; `audio`/`project_audio` → extract project id from `id` or colon form; `marker` → `NavigateToProjectAsync(metadata["project_id"])` when present.
   - **ScriptEditor:** `script` → `NavigateToScriptAsync`.
8. **Stop-short points (pre-pass):** Success toast even when selection skipped; unknown mapper types became empty string; marker never selected project; no distinction between host missing vs not navigable.

---

## 10. Current defects / coherence gaps (addressed in Pass 03)

| ID | Symptom | Cause | Severity | Resolution |
|----|---------|-------|----------|------------|
| D1 | “Navigation complete” when item not focused | Success toast unconditional after open | High | C3: success only if `NavigateToItemAsync` true; else warning/info |
| D2 | Unknown search types → empty `resultType` | `ToResultTypeString(Unknown)` → `""` | Med | C1: `ToPanelResultTypeString` passes through raw lowercased type |
| D3 | Marker opens timeline but no project context | Timeline ignored `marker` | Med | C6: use `searchMetadata` `project_id` |
| D4 | Non-navigable panel / null content | Silent success | Med | C5: Info/Warning toasts |
| D5 | Host findName fails | Silent or misleading | Low | Warning: shell could not locate host |

---

## 11. Execution record (closure)

| Item | Detail |
|------|--------|
| **Behavior** | Search navigation honors selection outcome; step-specific toasts; metadata to panels; marker→project when `project_id` set; `ToPanelResultTypeString` for unknown API types. |
| **Files changed** | `src/VoiceStudio.App/Services/SearchOverlayCoordinator.cs`; `src/VoiceStudio.Core/Panels/SearchResultTypeMapper.cs`; panel code-behinds: `TimelineView`, `LibraryView`, `ProfilesView`, `AnalyzerView`, `ScriptEditorView` under `src/VoiceStudio.App/Views/Panels/`; `src/VoiceStudio.App.Tests/Services/SearchOverlayCoordinatorTests.cs`; `src/VoiceStudio.App.Tests/Core/SearchResultTypeMapperTests.cs` |
| **Tests** | `dotnet test ... --filter "FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~SearchResultTypeMapperTests"` — 18 passed |
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` |
| **verify.ps1 -Quick** | **Authoritative:** `artifacts/verify/20260324_030133` (PASSED). Confirmed via `artifacts/verify/latest_pointer.json` (`overall_status: PASSED`). Report: `artifacts/verify/20260324_030133/verification_report.md`. |
| **Known leftovers** | `PanelNavigationTestHook` public for tests — production must leave null. E2E not added. `ISelectionBroadcastService` still not wired from global search. JsonElement values in metadata: rely on `ToString()` for `project_id`. |

---

## 12. Audit-grade coverage matrix and residual gaps

| Behavior | Owner | Test(s) | Proof source | Residual gap |
|----------|-------|---------|--------------|--------------|
| Result type → panel id | `SearchResultTypeMapper` | `SearchResultTypeMapperTests` | Unit | New API types need explicit mapper coverage when added |
| Routing / unknown type string | `SearchOverlayCoordinator` | Coordinator tests | Unit | — |
| Panel open failure | Coordinator + shell | Coordinator tests | Unit | — |
| Missing `PanelHost` / name lookup | Coordinator | Coordinator tests | Unit | — |
| Content null (lazy panel) | Coordinator | Coordinator tests | Unit | Timing-dependent; tests use mocks |
| `INavigatablePanel` false / non-navigable | Coordinator | Coordinator tests | Unit | — |
| Selection success → success toast only | Coordinator | Coordinator tests | Unit | — |
| Empty item id | Coordinator | Coordinator tests | Unit | — |
| Metadata forwarded to panel | Coordinator | Coordinator tests | Unit | JsonElement → string via `ToString()` may be brittle for edge shapes |
| Marker → project (`project_id` in metadata) | `TimelineView.xaml.cs` | Indirect via coordinator tests + manual | Code review | No dedicated Timeline unit test for every marker shape |
| Focus after navigate | Coordinator (best-effort) | Not strongly asserted | Logged warning on failure | Focus is best-effort; no E2E in this pass |

**Residual weaknesses (explicit):**

- **`PanelNavigationTestHook`** is for unit tests only; production must leave it null.
- **`ISelectionBroadcastService`** is not driven from global search in this pass.
- **No WinAppDriver / E2E** for search → panel → selection in Pass 03.
- **Focus** depends on WinUI/visual tree timing; failures are logged, not guaranteed impossible.

---

## 8. Likely failure modes (operational)

- Lazy panel: content null briefly after open → warning “not ready yet.”
- Startup not ready: `OpenPanelByIdAsync` false → existing error path.
- Incomplete verify run: do not update STATE as complete without `verification_report.md` and pointer advance.

---

## User-visible wins

- Search behaves like “jump to thing” with honest feedback when only the panel opens or selection fails.

---

## Changelog

| Date | Note |
|------|------|
| 2026-03-24 | Scope frozen (planning). |
| 2026-03-24 | Implementation + tests + doc closure. |
| 2026-03-24 | Governance reconciliation: canonical `SearchResultTypeMapper` path (`VoiceStudio.Core/Panels`); §12 coverage matrix; [WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md](WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md). |
