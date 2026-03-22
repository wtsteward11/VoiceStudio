# PR-8: Telemetry/Diagnostics-Adjacent BackendClient Extraction — Scope

**Status:** Scoped (2026-03-22)  
**Prerequisite:** Tasks 1-6 of governance repair complete  
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)

---

## Objective

Continue BackendClient strangling with a telemetry/diagnostics-adjacent slice. PR-5 (Health/Version) and PR-6 (Telemetry) are done; PR-8 reduces remaining coupling in DiagnosticsClient, StatusBar, and related call paths.

---

## Current State

- **DiagnosticsClient** already uses `IHealthVersionClient` and `ITelemetryClient` for health, telemetry, and traces.
- **Remaining BackendClient dependency:** `_backend.IsConnected` and `_backend.CircuitState` (for `GetConnectionStatus()`).
- **StatusBarActivityService** and **MainWindow.StatusBar** use health/telemetry clients; no direct script-editor or diagnostics HTTP in BackendClient.

---

## PR-8 Candidate Options

| Option | Scope | Methods | Complexity |
|--------|-------|---------|------------|
| **A. DiagnosticsClient decoupling** | Extract `IsConnected` / `CircuitState` to `IConnectionStatusClient` or expose via `IHealthVersionClient` | 0 new HTTP; refactor only | Low |
| **B. Macros extraction** | `/api/macros`, `/api/automation` (~11 methods) | GetMacrosAsync, CreateMacroAsync, etc. | Medium |
| **C. Workflows extraction** | `/api/workflows` (~6 methods) | GetWorkflowsAsync, ExecuteWorkflowAsync, etc. | Medium |
| **D. Models extraction** | `/api/models`, `/api/engine` (~9 methods) | GetModelsAsync, RegisterModelAsync, etc. | Medium |

**Recommended for PR-8:** Option A (DiagnosticsClient decoupling) — smallest blast radius, completes the diagnostics cluster. If A is too small, Option B (Macros) is the next-largest domain slice.

---

## PR-8 Option A — Detailed Scope

### Goal

Remove DiagnosticsClient's dependency on `IBackendClient` for `IsConnected` and `CircuitState`.

### Options

1. **Extend IHealthVersionClient** with `bool IsConnected` and `CircuitState CircuitState` — HealthVersionClient already does health checks; it could own connection status.
2. **Create IConnectionStatusClient** — New interface for `IsConnected`, `CircuitState`; BackendClient implements it; DiagnosticsClient takes both IHealthVersionClient and IConnectionStatusClient.
3. **Inject BackendClient only for status** — Keep as-is; document that DiagnosticsClient's BackendClient use is status-only (minimal).

### Recommendation

Option 2 (IConnectionStatusClient) — clean separation; BackendClient already has these properties; extraction is a one-line delegation.

---

### Frozen Extraction Contract (2026-03-22)

1. **Exact methods leaving IBackendClient:** `bool IsConnected { get; }` (remove). Note: `CircuitState` is not on `IBackendClient` — DiagnosticsClient currently casts `_backend` to `BackendClient` to read it; extraction eliminates that cast.

2. **Destination interface:** `IConnectionStatusClient` with:
   - `bool IsConnected { get; }`
   - `CircuitState CircuitState { get; }` (from `VoiceStudio.App.Utilities.RetryHelper`)

3. **Implementation:** `ConnectionStatusClient` — takes `BackendHttpContext`, delegates to `_context.Pipeline.IsConnected` and `_context.Pipeline.CircuitState`. No HTTP; exposes existing pipeline state. Same pattern as HealthVersionClient.

4. **Call sites that will switch:**
   - DiagnosticsClient — replace `IBackendClient` with `IConnectionStatusClient`
   - SystemStore — add `IConnectionStatusClient` for `IsBackendConnected`; retain `IBackendClient` only for `BaseAddress` cast (out of scope to remove)

5. **What stays in BackendClient:** BackendClient retains `IsConnected`/`CircuitState` via `_pipeline` internally; they are removed from **IBackendClient** only. BackendClient does **not** implement `IConnectionStatusClient` — `ConnectionStatusClient` does, using shared pipeline.

6. **Proof requirements:** Build passes; targeted seam tests; `verify.ps1 -Quick` green; extraction inventory updated; no `IBackendClient.IsConnected` references remain.

### Method Inventory

| Current Method | Current Interface | Route/Endpoint | Callers | Extraction Destination | Migration Status |
|----------------|-------------------|---------------|---------|------------------------|------------------|
| `IsConnected` | IBackendClient | None (pipeline state) | DiagnosticsClient, SystemStore | IConnectionStatusClient | Done (2026-03-22) |
| `CircuitState` | BackendClient (cast only) | None (pipeline state) | DiagnosticsClient.GetConnectionStatus() | IConnectionStatusClient | Done (2026-03-22) |

---

## Acceptance Criteria (Option A)

- [x] `IConnectionStatusClient` interface with `IsConnected`, `CircuitState`
- [x] `ConnectionStatusClient` implements it (takes BackendHttpContext; delegates to pipeline)
- [x] DiagnosticsClient constructor takes `(IHealthVersionClient, ITelemetryClient, IConnectionStatusClient)` — no IBackendClient
- [x] SystemStore uses IConnectionStatusClient for IsBackendConnected
- [x] All DiagnosticsClient callers updated
- [x] IsConnected removed from IBackendClient; removed from BackendClient public surface
- [x] Tests pass; build succeeds
- [x] Extraction inventory updated

---

## Acceptance Criteria (Option B — Macros, if A deferred)

- [ ] `IMacroClient` with all macro/automation methods
- [ ] `MacroClient` uses `BackendClientHttpPipeline`
- [ ] Methods removed from IBackendClient/BackendClient
- [ ] Callers migrated
- [ ] Tests pass; extraction inventory updated

---

## Status

- [x] Scoped
- [x] In Progress
- [x] Complete (2026-03-22)

**Proof:** `artifacts/verify/20260322_044739/` (or latest); 29 targeted tests pass (DiagnosticsClient, ConnectionStatus, BackendClientTransportPolicy).
