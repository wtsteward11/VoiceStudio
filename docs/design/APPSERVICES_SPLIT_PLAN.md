# AppServices Decomposition Plan (DI-01)

> **Purpose:** Identify safe grouping boundaries for AppServices registration clusters. Do not heroic rewrite; plan first.
> **Date:** 2026-03-14
> **Gate:** Release-trust full lane **GREEN** (2026-03-20) — `docs/reports/release_trust_closure_20260320.md`. Safe to schedule DI modularization after rank-1 transport scope is defined in `FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md`.

---

## Current Structure

AppServices has three registration methods in `Initialize()`:

1. `RegisterCoreInfrastructure` — correlation, config, metrics, coordinator, HttpClient
2. `RegisterBackendFacades` — BackendClient + all domain facades (~60+)
3. `RegisterUIServices` — DialogService, PanelRegistry, ViewModel factory, stores, etc.

---

## Proposed Boundaries

### 1. Core Infrastructure (unchanged)

| Service | Purpose |
|---------|---------|
| ICorrelationIdProvider | Request tracing |
| BackendClientConfig | API host/port |
| IRequestMetricsService | Request metrics |
| IRequestCoordinator | Single-flight, TTL |
| GracefulDegradationService | 429/502/503 handling |
| HttpClient | HTTP transport |

**Boundary:** No backend facades. No UI dependencies.

---

### 2. Backend Facades (split candidate)

**Group A — Core domain:** `IBackendClient`, `IProfilesClient`, `IProjectsClient`  
**Group B — Timeline:** `ITimelineClipService`, `ITimelineTrackService`, `ITimelineTranscriptionService`, `ITimelineSynthesisService`, `IProjectAudioClient`  
**Group C — Synthesis/quality:** `IVoiceSynthesisService`, `IEnginesClient`, `IQualityPipelineService`, `IEnsembleService`, `ITextAnalysisService`, `IQualityHistoryService`  
**Group D — Panel facades:** All `I*Client` (Profiles, Projects, Transcription, Training, BatchProcessing, etc.) — ~40+ clients  
**Group D-health:** Health/version/diagnostics cluster — `IHealthVersionClient`, `IDiagnosticsClient`, `IPluginHealthClient`; coherent registration cluster candidate for future split.  
**Group E — WebSocket:** `IWebSocketService`, `IWebSocketClientFactory`  
**Group F — Use cases:** `IProfilesUseCase`

**Safe split:** Extract into `RegisterBackendFacadesCore`, `RegisterBackendFacadesTimeline`, `RegisterBackendFacadesPanelClients` — or keep as single method with clear sections. Splitting reduces file size but increases coupling risk if order changes.

**Recommendation:** Keep single `RegisterBackendFacades` — add section comments for clarity. Splitting would require careful dependency ordering.

---

### 3. UI Services (split candidate)

**Group A — Core UI:** `IViewModelContext`, `IDialogService`, `ISettingsService`, `IPanelRegistry`, `PanelStateService`, `INavigationService`  
**Group B — Error/logging:** `IErrorDialogService`, `IErrorLoggingService`, `IAuditLoggingService`  
**Group C — Stores:** `AudioStore`, `StatePersistenceService`, `StateCacheService`, `OperationQueueService`  
**Group D — Panel architecture:** `IEventAggregator`, `IContextManager`, `ILayoutService`, `IWorkspaceService`, `IAppStateStore`, `ISelectionStack`, `IDragDropService`  
**Group E — Feature services:** `IHelpOverlayService`, `IUpdateService`, `IAudioPlayerService`, `IProfilePreviewService`, etc.  
**Group F — Command/UI:** `ToolbarConfigurationService`, `KeyboardShortcutService`, `IUnifiedCommandRegistry`, `CommandRouter`, `ICommandQueueService`  
**Group G — Module/plugin:** `PluginManager`, `IPluginBridgeService`, `ModuleLoader`  
**Group H — Infrastructure:** `IProjectRepository`, `ISecretsService`, `IErrorCoordinator`, `IViewModelFactory`

**Safe split:** Extract `RegisterUIStores`, `RegisterUIPanelArchitecture`, `RegisterUIFeatureServices` — or keep as single method with section comments.

**Recommendation:** Keep single `RegisterUIServices` — add section comments. Splitting is low value; file is ~250 lines.

---

### 4. Panel Services (separate)

`RegisterAllPanels()` runs after DI build. It is already separate. No change.

---

## Action Items

| # | Action | Risk |
|---|--------|------|
| 1 | Add section comments to `RegisterBackendFacades` (Core, Timeline, Panel facades) | Low |
| 2 | Add section comments to `RegisterUIServices` (Core, Stores, Panel arch, Feature) | Low |
| 3 | Document dependency order in plan (e.g., IBackendClient before all facades) | Low |
| 4 | **Defer** actual split into multiple methods — no rewrite until proven need | — |

---

## Dependency Order (Critical)

1. `RegisterCoreInfrastructure` — must run first
2. `RegisterBackendFacades` — depends on Core (HttpClient, Config, Coordinator)
3. `RegisterUIServices` — depends on Backend (some services need IBackendClient)
4. `RegisterAllPanels` — must run after container is built

---

## Composition-Root Discipline (Architecture Wave 2026-03-21)

**Rule:** No new `Register*` unless a real extraction forces it.

For each new seam extraction:

- Add registrations **only when necessary** — no speculative registration.
- **Document why** each new registration exists (e.g., "SearchClient requires BackendClientHttpPipeline; PR-4 extraction").
- **Prefer grouped/facade-based registration** — use `BackendHttpContext.Pipeline` for shared pipeline clients; avoid per-client HttpClient factories.
- **Do not** let MainWindow refactors drag in new service-locator patterns.

**Pressure log:** See `docs/design/APPSERVICES_PRESSURE_LOG.md` for every new registration demanded by architecture work.

---

## Conclusion

**Do not heroic rewrite.** The current AppServices structure is acceptable. Add section comments for maintainability. Revisit split only if:
- File exceeds 500 lines
- A specific cluster needs independent testing
- A new registration pattern requires isolation
