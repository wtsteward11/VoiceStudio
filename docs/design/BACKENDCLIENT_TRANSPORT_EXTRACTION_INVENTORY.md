# BackendClient transport extraction inventory

**Purpose:** Line-anchored map of [`BackendClient.cs`](../../src/VoiceStudio.App/Services/BackendClient.cs) for PR-1 (policy-only extraction) vs later PRs. **Not** migration-complete claims.  
**Date:** 2026-03-20  
**Related:** [`FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md`](FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md) §1, [`APPSERVICES_SPLIT_PLAN.md`](APPSERVICES_SPLIT_PLAN.md)

---

## Reconciliation: `BackendTransport` vs `BackendClient`

| Surface | Location | Pattern |
|--------|----------|---------|
| **BackendClient** | `Services/BackendClient.cs` | Throws `BackendException` subclasses; `ExecuteWithRetryAsync` + `CircuitBreaker` + connection flags |
| **BackendTransport** | `Services/Gateways/BackendTransport.cs` + `IBackendTransport` | Returns `GatewayResult<T>`; parallel `ExecuteAsync`, `CreateErrorFromResponseAsync`, `HandleResponseAsync` |

**PR-1 convergence strategy (frozen):** Extract an **`internal`** [`BackendClientHttpPipeline`](../../src/VoiceStudio.App/Services/BackendClientHttpPipeline.cs) that preserves **BackendClient’s throw-based contract** and shares **one** `HttpClient` instance. **Do not** route `BackendClient` through `IBackendTransport` in PR-1 (would force `GatewayResult` at the façade or a behavior-changing adapter).

**PR-2 (done 2026-03-20):** Shared `StandardErrorResponseParser` — `BackendClientHttpPipeline.CreateExceptionFromResponseAsync` and `BackendTransport.CreateErrorFromResponseAsync` both call it; no `GatewayResult` in BackendClient.

---

## Bucket A — Transport policy

| Item | Approx. lines | Notes |
|------|---------------|--------|
| Handler chain in ctor | 256–274 | `DegradedModeClearHandler` → `RequestMetricsHandler` → `CorrelationIdHandler` → inner |
| `HttpClient` construction | 270–274 | `BaseAddress`, `Timeout = config.RequestTimeout` |
| `_circuitBreaker` field + init | 226–227, 279–280 | Threshold 5, 30s reset |
| `MaxRetries`, `RetryDelayMs` | 227–228 | Used by retry path |
| Connection fields | 230–233 | `_isConnected`, `_lastConnectionCheck`, `ConnectionCheckIntervalSeconds` |
| `IsConnected` | 309 | |
| `CircuitState` | 319 | |
| `CheckHealthAsync` | 439–454 | Direct GET `/api/health`, mutates connection flags |
| `ExecuteWithRetryAsync<T>` | 1296–1340 | `UpdateConnectionStatusAsync` + `_circuitBreaker` + `RetryHelper.ExecuteWithExponentialBackoffAsync` |
| `UpdateConnectionStatusAsync` | 1345–1363 | Periodic GET `/api/health` |
| `TryCheckHealthAsync` (static) | 462–479 | **Smell:** `AppServices.GetService<HttpClient>()` — **PR-1 fix:** dedicated short-lived `HttpClient` for probe |

**Cross-ref `BackendTransport`:** ctor 48–65; `ExecuteAsync` wraps circuit + retry (different API shape).

---

## Bucket B — Serialization / response handling

| Item | Approx. lines | Notes |
|------|---------------|--------|
| `_jsonOptions` | 225, 277 | `JsonSerializerOptionsFactory.BackendApi` |
| `SendRequestAsync` (POST JSON) | 321–350 | Serialize body, `PostAsync`, deserialize |
| `SendRequestAsync` (method overload) | 355–428 | GET/POST/PUT/DELETE branches |
| `GetAsync<T>` | 3162–3184 | |
| `PostAsync<TRequest,TResponse>` | 3189–3219 | |
| `PostAsync<TRequest>` (void) | 3224–3240 | |
| `PutAsync<TRequest,TResponse>` | 3245–3271 | |
| `CreateExceptionFromResponseAsync` | 1365–1450 | StandardErrorResponse → `BackendException` hierarchy + `IsRetryable` |

**Cross-ref `BackendTransport`:** `HandleResponseAsync` 207–237; `CreateErrorFromResponseAsync` 239+ (`GatewayError`).

---

## Bucket C — Unsafe / global concerns

| Concern | Location | PR-1 action |
|---------|----------|-------------|
| Static service locator | `TryCheckHealthAsync` → `AppServices.GetService<HttpClient>()` | Remove; use `new HttpClient` + absolute URL + timeout |
| Shared `HttpClient.Timeout` mutation | `UploadFilesWithProgressAsync` 4353–4371 | Replace with **`CancellationTokenSource.CreateLinkedTokenSource`** + `CancelAfter(timeout)`; never mutate `_httpClient.Timeout` |
| Connection side effects inside retry path | `ExecuteWithRetryAsync` / `UpdateConnectionStatusAsync` | Moved with pipeline; behavior unchanged |

**Callers of `TryCheckHealthAsync`:** `MainWindow.Smoke.cs` (~473, 477, 790, 794, 969), `App.xaml.cs` (~1287).

---

## Bucket D — Feature surfaces (by `/api/` prefix)

Grouped by first path segment after `/api/`. Endpoint **methods** stay in `BackendClient` for PR-1; only policy/helpers move.

| Prefix / area | Examples (non-exhaustive) |
|---------------|---------------------------|
| **Health / version** | `/api/health`, `/api/version/`, `/api/version/compatibility` |
| **MCP** | `/api/mcp/{operation}` |
| **Voice** | `/api/voice/synthesize`, `analyze`, `clone`, `audio/{id}` |
| **Audio** | `/api/audio/file`, `export`, `formats`, `upload`, waveform/spectrogram/meters/radar/loudness/phase |
| **Projects + timeline** | `/api/projects/.../audio`, `tracks`, `clips`, `markers` |
| **Macros** | `/api/macros`, `automation` |
| **Workflows** | `/api/workflows` |
| **Models / engine** | `/api/models`, `/api/engine/telemetry` |
| **Effects** | `/api/effects/chains`, `presets` |
| **Batch** | `/api/batch/jobs`, `queue`, `quality` |
| **Transcribe** | `/api/transcribe/` |
| **Training** | `/api/training/...` |
| **Ensemble** | `/api/ensemble/multi-engine` |
| **Mixer** | `/api/mixer/state`, `presets`, sends/returns/subgroups/master/channels |
| **Video** | `/api/video/generate`, `upscale`, `engines`, `{id}`, `voice/convert` |
| **Quality** | `/api/quality/...` (via `PostAsync` helpers ~3559+) |
| **Script editor** | `/api/script-editor/...` |
| **Pipeline** | `/api/pipeline/...` |
| **Plugins** | `api/plugins/...` (some paths omit leading `/` — invariant risk, PR-2) |

**Regenerate hint:** `rg '/api/|api/plugins' src/VoiceStudio.App/Services/BackendClient.cs`

---

## PR scope freeze

### PR-1 — IN (no public API rename; no endpoint moves)

- New internal **`BackendClientHttpPipeline`**: `ExecuteWithRetryAsync`, `UpdateConnectionStatusAsync`, `CreateExceptionFromResponseAsync`, `SendRequestAsync` (both), `GetAsync`, `PostAsync` (both), `PutAsync`.
- **`BackendClient`** retains ctor handler chain, `_httpClient`, `_config`, `_requestCoordinator`, `WebSocketService`, all feature methods; delegates generic HTTP + retry to `_pipeline`.
- **Fix:** `TryCheckHealthAsync` (no `AppServices`).
- **Fix:** upload timeout via linked CTS, not `_httpClient.Timeout` mutation.

### PR-1 — OUT

- Re-grouping endpoint methods by domain.
- Multipart / streaming one-offs (except timeout fix in existing upload method).
- `AppServices` registration splits.
- `MainWindow` decomposition.
- Switching `BackendClient` to `IBackendTransport` / `GatewayResult`.

### PR-2 (done 2026-03-20)

- Shared `StandardErrorResponseParser` — `BackendClientHttpPipeline` + `BackendTransport` use it.
- Path normalization: `/api/plugins` (leading slash) enforced; `GetPluginHealthDashboardAsync_ResolvesCorrectPath` test.

### PR-3 — Plugin health extraction (done 2026-03-21)

**Current state (2026-03-21):** BackendClient.cs = **4110 lines**. Bucket D: ~100 endpoint references.

**Single next extraction:** `/api/plugins/` methods → dedicated `IPluginHealthClient` / `PluginHealthClient`.

| Method | Lines | Notes |
|--------|-------|-------|
| `GetPluginHealthDashboardAsync` | 4050–4066 | Uses `SendRequestAsync`; path `/api/plugins/health/dashboard` |
| `GetPluginMetricsAsync` | 4068–4085 | Uses `SendRequestAsync`; path `/api/plugins/{id}/metrics` |
| `ExportPluginMetricsAsync` | 4087–4101 | Uses `_httpClient.GetAsync` directly (not pipeline); path `/api/plugins/metrics/export` |

**PR-3 — IN**

- New `IPluginHealthClient` interface with `GetPluginHealthDashboardAsync`, `GetPluginMetricsAsync`, `ExportPluginMetricsAsync`.
- New `PluginHealthClient` implementing `IPluginHealthClient`; uses `BackendClientHttpPipeline` or shared `HttpClient`/`SendRequestAsync` pattern.
- Migrate `ExportPluginMetricsAsync` to use pipeline (replace raw `_httpClient.GetAsync`).
- `BackendClient` delegates plugin health calls to `IPluginHealthClient` (injected) or removes methods and updates callers.
- Test migration: `GetPluginHealthDashboardAsync_ResolvesCorrectPath` → `PluginHealthClient` or equivalent.

**PR-3 — OUT**

- Other domain extractions (voice, audio, training, etc.).
- BackendClient facade changes beyond plugin methods.
- Changing `IBackendClient` surface for non-plugin callers.

**PR-3 — DONE (2026-03-21):** `PluginHealthClient` owns HTTP via `BackendClientHttpPipeline`; `BackendHttpContext` shared with `BackendClient`; 3 methods removed from `BackendClient`/`IBackendClient`; `GetPluginHealthDashboardAsync_ResolvesCorrectPath` tests `PluginHealthClient`.

### PR-4 — Search extraction (done 2026-03-21)

**Single extraction:** `/api/search` → `ISearchClient` / `SearchClient`.

| Method | Notes |
|--------|-------|
| `SearchAsync` | Removed from `BackendClient`/`IBackendClient`; `SearchClient` owns HTTP via `BackendClientHttpPipeline` |

**PR-4 — IN**

- `SearchClient` uses `BackendClientHttpPipeline.GetAsync<SearchResponse>`; internal ctor for testability.
- `ISearchClient` registered with `BackendHttpContext.Pipeline` in AppServices.
- `SearchAsync` removed from `IBackendClient`, `BackendClient`, `MockBackendClient`.
- Test: `SearchAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.

**PR-4 — OUT**

- Other domain extractions (voice, audio, training, etc.).

**PR-5 — Health/Version extraction (done 2026-03-21)**

**Single extraction:** `/api/health`, `/api/version/`, `/api/version/compatibility` → `IHealthVersionClient` / `HealthVersionClient`.

| Method | Notes |
|--------|-------|
| CheckHealthAsync | Removed from BackendClient; HealthVersionClient uses pipeline.CheckHealthAsync |
| CheckApiVersionAsync | Removed from BackendClient; HealthVersionClient owns |
| GetApiVersionInfoAsync | Removed from BackendClient; HealthVersionClient owns |
| ValidateApiVersionOnStartupAsync | Removed from BackendClient; HealthVersionClient owns |

**PR-5 — IN**

- Migrated callers: DiagnosticsClient, StatusBarActivityService, DeferredServiceInitializer, MainWindow.StatusBar, DiagnosticsView (TestConnection, Reconnect).
- **Retained exception:** `BackendClient.TryCheckHealthAsync` (static) for bootstrap/self-test; callers: App.xaml.cs, MainWindow.Smoke.cs. Instance health uses IHealthVersionClient.
- **Proof/tests:** `BackendClientTransportPolicyTests.CheckHealthAsync_FailureThenRecovery_ConnectionStateCorrect`, `CheckHealthAsync_ResolvesCorrectPath`; `StatusBarActivityServiceTests`; `DiagnosticsViewModelSeamTests`; `DiagnosticsClientTests.CheckHealthAsync_DelegatesToHealthVersionClient`; `StatusBarActivityServiceTests.StartMonitoring_CallsHealthVersionClient_WhenMonitoring`; `DeferredServiceInitializerTests.BackendHealthCheck_InvokesIHealthVersionClient_WhenRegistered`. PR-5-focused filter: 32+ tests pass; verify.ps1 -Quick verified pass.
- **Sweep:** No remaining `backendClient.CheckHealthAsync` or `_backend.CheckHealthAsync`; version methods only on IHealthVersionClient/HealthVersionClient. Boundary clean.

### PR-6 — Telemetry/diagnostics extraction (done 2026-03-21)

**Single extraction:** `/api/engine/telemetry`, `/api/v1/diagnostics/traces` → `ITelemetryClient` / `TelemetryClient`.

| Method | Notes |
|--------|-------|
| GetTelemetryAsync | Removed from BackendClient; TelemetryClient owns `/api/engine/telemetry` |
| GetTracesAsync | TelemetryClient owns `/api/v1/diagnostics/traces`; DiagnosticsClient delegates to ITelemetryClient |

**PR-6 — IN**

- `ITelemetryClient` interface with `GetTelemetryAsync`, `GetTracesAsync`.
- `TelemetryClient` uses `BackendClientHttpPipeline` via `BackendHttpContext.Pipeline`.
- `DiagnosticsClient` constructor takes `(IConnectionStatusClient, IHealthVersionClient, ITelemetryClient)` (PR-8); delegates telemetry/traces to `_telemetry`.
- `MainWindow.StatusBar` uses `GetTelemetryClient()` for GPU/VRAM display.
- `GetTelemetryAsync` removed from `IBackendClient`, `BackendClient`.
- Tests: `DiagnosticsClientTests.GetTelemetryAsync_DelegatesToTelemetryClient`, `GetTracesAsync_DelegatesToTelemetryClient`.

**PR-6 — OUT**

- Other domain extractions (voice, audio, training, etc.).

### PR-7 — Script editor extraction (done 2026-03-21)

**Single extraction:** `/api/script-editor/` → `IScriptEditorClient` / `ScriptEditorClient` (already existed; migrated from IBackendClient delegation to pipeline ownership).

| Method | Notes |
|--------|-------|
| GetScriptsAsync | ScriptEditorClient owns; uses pipeline.GetAsync |
| GetScriptAsync | ScriptEditorClient owns |
| CreateScriptAsync | ScriptEditorClient owns |
| UpdateScriptAsync | ScriptEditorClient owns |
| DeleteScriptAsync | ScriptEditorClient owns |
| AddSegmentToScriptAsync | ScriptEditorClient owns |
| RemoveSegmentFromScriptAsync | ScriptEditorClient owns |

**PR-7 — IN**

- ScriptEditorClient uses `BackendClientHttpPipeline` via `BackendHttpContext.Pipeline`; no IBackendClient.
- AppServices: `services.AddSingleton<IScriptEditorClient>(sp => new ScriptEditorClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 7 methods removed from `IBackendClient`, `BackendClient`.
- Test: `GetScriptsAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- **Proof (closure-grade 2026-03-22):**
  - `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS
  - `dotnet test ... --filter "FullyQualifiedName~ScriptEditor"` → 24 passed, 0 failed, 0 skipped
  - verify.ps1 -Quick artifact: `artifacts/verify/20260322_011101/verification_report.md` (or `artifacts/verify/latest/`)
- **Sweep (2026-03-22):** IBackendClient clean; BackendClient.cs clean; all callers route through IScriptEditorClient. No orphaned usage.
  - grep `backendClient.GetScript`, `_backend.GetScript`, `script-editor` in BackendClient.cs → no matches
  - grep `IBackendClient.*GetScript` in src → no matches
  - ScriptEditorViewModel, ScriptEditorView, tests use `_scriptEditorClient` / `IScriptEditorClient` only

### PR-8 — Connection status extraction (done 2026-03-22)

**Single extraction:** `IsConnected` / `CircuitState` → `IConnectionStatusClient` / `ConnectionStatusClient` (pipeline state, no HTTP).

| Method | Notes |
|--------|-------|
| IsConnected | Removed from IBackendClient; ConnectionStatusClient delegates to pipeline.IsConnected |
| CircuitState | Removed from BackendClient public surface; ConnectionStatusClient delegates to pipeline.CircuitState |

**PR-8 — IN**

- `IConnectionStatusClient` interface with `IsConnected`, `CircuitState` (from `VoiceStudio.App.Utilities.RetryHelper`).
- `ConnectionStatusClient` takes `BackendHttpContext`; delegates to `Pipeline.IsConnected` and `Pipeline.CircuitState`. No HTTP.
- `DiagnosticsClient` constructor takes `(IConnectionStatusClient, IHealthVersionClient, ITelemetryClient)` — no IBackendClient.
- `SystemStore` uses `IConnectionStatusClient` for `IsBackendConnected`; retains `IBackendClient` for `BaseAddress` cast only.
- `IsConnected` and `CircuitState` removed from `IBackendClient` and `BackendClient` public surface.
- Tests: `ConnectionStatusClientTests.ConnectionStatusClient_DelegatesToPipeline_IsConnectedAndCircuitState`, `IBackendClient_DoesNotExposeIsConnected`; `BackendClientTransportPolicyTests.CheckHealthAsync_FailureThenRecovery_ConnectionStateCorrect` uses `IConnectionStatusClient`.
- **Proof (2026-03-22):** `dotnet build` PASS; `dotnet test --filter "FullyQualifiedName~DiagnosticsClient|FullyQualifiedName~ConnectionStatus|FullyQualifiedName~BackendClientTransportPolicy"` 29 passed; verify.ps1 -Quick artifact: `artifacts/verify/20260322_040007/verification_report.md`.
- **Ownership Sweep (2026-03-22):** Repo-wide `rg "IsConnected|CircuitState" src/` — classified all hits. Seam boundary clean.

| Location | Classification | Action |
|----------|-----------------|--------|
| IConnectionStatusClient, ConnectionStatusClient, DiagnosticsClient, SystemStore | Owned by IConnectionStatusClient | None |
| BackendClientHttpPipeline, RetryHelper.CircuitBreaker | Internal pipeline concern | None |
| WebSocketService, JobProgressWebSocketClient, MeterWebSocketClient, PipelineStreamingWebSocketClient, RealtimeVoiceWebSocketClient | Different seam (WebSocket) | None |
| BackendTransport, IBackendTransport, MockBackendTransport | Different transport (parallel to BackendClient) | None |
| AppState, StateSelectors, StoreIntegration, DiagnosticsViewModel | Derived from SystemStore/DiagnosticsClient | None |
| IDiagnosticsClient.IsConnected | Facade over IConnectionStatusClient | None |

No stale backend connection-status usage. No migration required.

### PR-9 — Macros extraction (done 2026-03-22)

**Single extraction:** `/api/macros`, `/api/macros/automation` → `IMacroClient` / `MacroClient` (already existed; migrated from IBackendClient delegation to pipeline ownership).

| Method | Notes |
|--------|-------|
| GetMacrosAsync | MacroClient owns; uses pipeline.GetAsync |
| GetMacroAsync | MacroClient owns |
| CreateMacroAsync | MacroClient owns |
| UpdateMacroAsync | MacroClient owns |
| DeleteMacroAsync | MacroClient owns |
| ExecuteMacroAsync | MacroClient owns |
| GetMacroExecutionStatusAsync | MacroClient owns |
| GetAutomationCurvesAsync | MacroClient owns |
| CreateAutomationCurveAsync | MacroClient owns |
| UpdateAutomationCurveAsync | MacroClient owns |
| DeleteAutomationCurveAsync | MacroClient owns |

**PR-9 — IN**

- `MacroClient` uses `BackendClientHttpPipeline` via DI; internal ctor for testability.
- AppServices: `services.AddSingleton<IMacroClient>(sp => new MacroClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 11 methods removed from `IBackendClient`, `BackendClient`.
- Test: `GetMacrosAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- **Proof (2026-03-22):** Artifact: `artifacts/verify/20260322_040007/verification_report.md` (PASS). See [PR-9_ARTIFACT_RECONCILIATION.md](PR-9_ARTIFACT_RECONCILIATION.md).
- **Call sites:** MacroViewModel, MacroActions (CreateMacroAction, DeleteMacroAction, etc.), AutomationActions, MacroView. MacroViewModelSeamTests mocks IMacroClient.
- **Seam tests:** `GetMacrosAsync_ResolvesCorrectPath` (BackendClientTransportPolicyTests); `IBackendClient_DoesNotExposeMacroMethods`, `BackendClient_DoesNotExposeMacroMethods` (BackendClientExtractionRegressionTests).
- **Ownership Sweep (2026-03-22):**

| Search | Result |
|--------|--------|
| `rg "GetMacrosAsync|GetMacroAsync|...|DeleteAutomationCurveAsync" src/ --glob "**/IBackendClient*.cs"` | Zero hits |
| `rg "GetMacrosAsync|...|DeleteAutomationCurveAsync" src/ --glob "**/BackendClient.cs"` | Zero hits |
| `rg "_backend\.(GetMacro|CreateMacro|...|DeleteAutomationCurve)" src/` | Zero hits |

No macro methods on IBackendClient or BackendClient. No stale callers. Seam boundary clean.

**PR-9 — OUT**

- Macros UI changes; Workflows/Models bundling.

### PR-10 — Workflows extraction (done 2026-03-22)

**Single extraction:** `/api/workflows` → `IWorkflowAutomationClient` / `WorkflowAutomationClient` (already existed; migrated from IBackendClient delegation to pipeline ownership).

| Method | Notes |
|--------|-------|
| GetWorkflowsAsync | WorkflowAutomationClient owns; uses pipeline.GetAsync |
| GetWorkflowAsync | WorkflowAutomationClient owns |
| CreateWorkflowAsync | WorkflowAutomationClient owns |
| UpdateWorkflowAsync | WorkflowAutomationClient owns |
| DeleteWorkflowAsync | WorkflowAutomationClient owns |
| ExecuteWorkflowAsync | WorkflowAutomationClient owns |

**PR-10 — IN**

- `WorkflowAutomationClient` uses `BackendClientHttpPipeline` via DI; internal ctor for testability.
- AppServices: `services.AddSingleton<IWorkflowAutomationClient>(sp => new WorkflowAutomationClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 6 methods removed from `IBackendClient`, `BackendClient`.
- Test: `GetWorkflowsAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposeWorkflowMethods`, `BackendClient_DoesNotExposeWorkflowMethods` in `BackendClientExtractionRegressionTests`.
- **Proof (2026-03-22):** Build PASS; targeted tests (Workflow, BackendClientExtractionRegression) 85 passed; verify.ps1 -Quick artifact: `artifacts/verify/20260322_130417/verification_report.md`. See [PR-10_ARTIFACT_RECONCILIATION.md](PR-10_ARTIFACT_RECONCILIATION.md).
- **Call sites:** WorkflowAutomationViewModel (only `IWorkflowAutomationClient _workflowClient`; zero IBackendClient). WorkflowAutomationViewModelSeamTests mocks IWorkflowAutomationClient.
- **Seam tests:** `GetWorkflowsAsync_ResolvesCorrectPath` (BackendClientTransportPolicyTests); `IBackendClient_DoesNotExposeWorkflowMethods`, `BackendClient_DoesNotExposeWorkflowMethods` (BackendClientExtractionRegressionTests).
- **Ownership Sweep (2026-03-22):**

| Search | Result |
|--------|--------|
| `rg "GetWorkflowsAsync|GetWorkflowAsync|CreateWorkflowAsync|UpdateWorkflowAsync|DeleteWorkflowAsync|ExecuteWorkflowAsync" src/ --glob "**/IBackendClient*.cs"` | Zero hits |
| `rg "GetWorkflowsAsync|...|ExecuteWorkflowAsync" src/ --glob "**/BackendClient.cs"` | Zero hits |
| `rg "_backend\.(GetWorkflow|CreateWorkflow|...|ExecuteWorkflow)" src/` | Zero hits |

| Location | Classification | Action |
|----------|-----------------|--------|
| IWorkflowAutomationClient, WorkflowAutomationClient | Owned by IWorkflowAutomationClient | None |
| WorkflowAutomationViewModel | Uses _workflowClient only; no IBackendClient | None |
| WorkflowAutomationViewModelSeamTests, BackendClientTransportPolicyTests, BackendClientExtractionRegressionTests | Test mocks / seam / anti-regression | None |

No workflow methods on IBackendClient or BackendClient. WorkflowAutomationViewModel uses IWorkflowAutomationClient exclusively. Caller audit: zero monolith leakage.

**PR-10 — OUT**

- Workflows UI changes; Effects bundling.

### PR-11 — Effects extraction (done 2026-03-22)

**Single extraction:** `/api/effects/chains`, `/api/effects/presets` (read) → `IEffectChainClient` / `EffectChainClient` (already existed; migrated from IBackendClient delegation to pipeline ownership).

| Method | Notes |
|--------|-------|
| GetEffectChainsAsync | EffectChainClient owns; uses pipeline.GetAsync |
| GetEffectChainAsync | EffectChainClient owns |
| CreateEffectChainAsync | EffectChainClient owns |
| UpdateEffectChainAsync | EffectChainClient owns |
| DeleteEffectChainAsync | EffectChainClient owns |
| ProcessAudioWithChainAsync | EffectChainClient owns |
| GetEffectPresetsAsync | EffectChainClient owns |

**PR-11 — IN**

- `EffectChainClient` uses `BackendClientHttpPipeline` via DI; internal ctor for testability.
- AppServices: `services.AddSingleton<IEffectChainClient>(sp => new EffectChainClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 7 methods removed from `IBackendClient`, `BackendClient`.
- Test: `GetEffectChainsAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposeEffectChainMethods`, `BackendClient_DoesNotExposeEffectChainMethods` in `BackendClientExtractionRegressionTests`.
- **Proof (2026-03-22):** Build PASS; targeted tests (EffectChain, EffectsMixer, BackendClientExtractionRegression) 34 passed; verify.ps1 -Quick artifact: `artifacts/verify/20260322_133436/verification_report.md`.
- **Call sites:** EffectsMixerViewModel (only `_effectChainClient`; zero IBackendClient for effect chain/preset). CreateEffectChainAction, DeleteEffectChainAction via ViewModel. EffectsMixerViewModelTests mocks IEffectChainClient.
- **Seam tests:** `GetEffectChainsAsync_ResolvesCorrectPath` (BackendClientTransportPolicyTests); `IBackendClient_DoesNotExposeEffectChainMethods`, `BackendClient_DoesNotExposeEffectChainMethods` (BackendClientExtractionRegressionTests).
- **Ownership Sweep (2026-03-22):**

| Search | Result |
|--------|--------|
| `rg "GetEffectChainsAsync|GetEffectChainAsync|CreateEffectChainAsync|UpdateEffectChainAsync|DeleteEffectChainAsync|ProcessAudioWithChainAsync|GetEffectPresetsAsync" src/ --glob "**/IBackendClient*.cs"` | Zero hits |
| `rg "GetEffectChainsAsync|...|GetEffectPresetsAsync" src/ --glob "**/BackendClient.cs"` | Zero hits |
| EffectsMixerViewModel, EffectsMixerViewModelTests | Use _effectChainClient / IEffectChainClient only | None |

No effect chain methods on IBackendClient or BackendClient. All callers use IEffectChainClient. **Retained on IBackendClient (until PR-12):** CreateEffectPresetAsync, DeleteEffectPresetAsync.

**PR-11 — OUT**

- Effect preset create/delete (extracted in PR-12).
- UI redesign.

### PR-12 — Effect presets create/delete (done 2026-03-22)

**Single extraction:** `CreateEffectPresetAsync`, `DeleteEffectPresetAsync` → `IEffectChainClient` / `EffectChainClient`. Presets stay in same effects seam (PR-11 recommended).

| Method | Notes |
|--------|-------|
| CreateEffectPresetAsync | EffectChainClient owns; POST /api/effects/presets |
| DeleteEffectPresetAsync | EffectChainClient owns; DELETE /api/effects/presets/{presetId} |

**PR-12 — IN**

- Added to `IEffectChainClient`; implemented in `EffectChainClient` via `BackendClientHttpPipeline`.
- Both methods removed from `IBackendClient`, `BackendClient`.
- Tests: `CreateEffectPresetAsync_ResolvesCorrectPath`, `DeleteEffectPresetAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposeEffectPresetMethods`, `BackendClient_DoesNotExposeEffectPresetMethods` in `BackendClientExtractionRegressionTests`.
- **Proof (2026-03-22):** Build PASS; targeted tests 36 passed; verify.ps1 -Quick artifact: `artifacts/verify/20260322_135530/verification_report.md`.
- **Call sites:** EffectsMixerViewModelTests only (no runtime ViewModel caller; preset create/delete exercised via mock).
- **Seam tests:** CreateEffectPresetAsync_ResolvesCorrectPath, DeleteEffectPresetAsync_ResolvesCorrectPath.
- **Ownership Sweep (2026-03-22):**

| Search | Result |
|--------|--------|
| `rg "CreateEffectPresetAsync|DeleteEffectPresetAsync" src/ --glob "**/IBackendClient*.cs"` | Zero hits |
| `rg "CreateEffectPresetAsync|DeleteEffectPresetAsync" src/ --glob "**/BackendClient.cs"` | Zero hits |
| IEffectChainClient, EffectChainClient, EffectsMixerViewModelTests | Valid ownership | None |

Effects domain fully extracted. No preset methods on IBackendClient or BackendClient.

**PR-12 — OUT**

- Models extraction (defer; reassess after PR-12).
- STATE.md bloat trim (secondary governance task).

### PR-13 — Pipeline (GetPipelineProvidersAsync, ProcessPipelineAsync) (done 2026-03-22)

**Single extraction:** `GetPipelineProvidersAsync`, `ProcessPipelineAsync` → `IPipelineConversationClient` / `PipelineConversationClient`. Uses `BackendClientHttpPipeline`.

**Exact caller ownership:** PipelineConversationViewModel only. Uses `IPipelineConversationClient _client`; no `IBackendClient` reference. PipelineConversationView resolves via `ServiceProvider.GetPipelineConversationClient()`.

| Method | Notes |
|--------|-------|
| GetPipelineProvidersAsync | PipelineConversationClient owns; GET `/api/pipeline/providers` |
| ProcessPipelineAsync | PipelineConversationClient owns; POST `/api/pipeline/process` |

**PR-13 — IN**

- `PipelineConversationClient` uses `BackendClientHttpPipeline` via DI; internal ctor `(pipeline, IWebSocketService?)`.
- AppServices: `services.AddSingleton<IPipelineConversationClient>(sp => new PipelineConversationClient(sp.GetRequiredService<BackendHttpContext>().Pipeline, sp.GetService<IWebSocketService>()))`.
- Both methods removed from `IBackendClient`, `BackendClient`.
- Tests: `GetPipelineProvidersAsync_ResolvesCorrectPath`, `ProcessPipelineAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposePipelineMethods`, `BackendClient_DoesNotExposePipelineMethods` in `BackendClientExtractionRegressionTests`.
- Scope: [PR-13_PIPELINE_SCOPE.md](PR-13_PIPELINE_SCOPE.md).
- **Authoritative proof path:** `artifacts/verify/20260322_143514/verification_report.md` (PR-13_ARTIFACT_RECONCILIATION.md).

**PR-13 — OUT**

- **What remains in pipeline domain:** GetPipelineMetricsAsync on IBackendClient (metrics UI). Intentional; different concern than providers/process.
- WebSocket streaming (different seam).

**Ownership Sweep (2026-03-22):**

| Search | Result |
|--------|--------|
| `rg "GetPipelineProvidersAsync\|ProcessPipelineAsync" src/.../IBackendClient.cs` | Zero method signatures (comment only at line 338) |
| `rg "GetPipelineProvidersAsync\|ProcessPipelineAsync" src/.../BackendClient.cs` | Zero method definitions (comment only at line 3184) |
| `rg "_backend\|IBackendClient" src/.../PipelineConversationViewModel.cs` | Zero hits |
| PipelineConversationViewModel | Uses IPipelineConversationClient only; _client.GetPipelineProvidersAsync, _client.ProcessPipelineAsync |

Pipeline domain (providers/process) fully extracted. No pipeline methods on IBackendClient or BackendClient.

### PR-14 — BackupRestore (7 methods) (done 2026-03-22)

**Single extraction:** `GetBackupsAsync`, `GetBackupAsync`, `CreateBackupAsync`, `DownloadBackupAsync`, `RestoreBackupAsync`, `UploadBackupAsync`, `DeleteBackupAsync` → `IBackupRestoreClient` / `BackupRestoreClient`. Uses `BackendClientHttpPipeline`.

**Exact caller ownership:** BackupRestoreViewModel only. Uses `IBackupRestoreClient _backupRestoreClient`; no `IBackendClient` reference.

| Method | Notes |
|--------|-------|
| GetBackupsAsync | GET `/api/backup` |
| GetBackupAsync | GET `/api/backup/{id}` |
| CreateBackupAsync | POST `/api/backup` |
| DownloadBackupAsync | GET `/api/backup/{id}/download` (stream via GetStreamAsync) |
| RestoreBackupAsync | POST `/api/backup/{id}/restore` |
| UploadBackupAsync | POST `/api/backup/upload` (multipart via PostMultipartAsync) |
| DeleteBackupAsync | DELETE `/api/backup/{id}` |

**PR-14 — IN**

- `BackupRestoreClient` uses `BackendClientHttpPipeline` via DI; internal ctor `(pipeline)`.
- Pipeline extended: `GetStreamAsync`, `PostMultipartAsync` (PR-14).
- AppServices: `services.AddSingleton<IBackupRestoreClient>(sp => new BackupRestoreClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 7 methods removed from `IBackendClient`, `BackendClient`.
- Tests: `GetBackupsAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposeBackupMethods`, `BackendClient_DoesNotExposeBackupMethods` in `BackendClientExtractionRegressionTests`.
- Scope: [PR-14_BACKUP_RESTORE_SCOPE.md](PR-14_BACKUP_RESTORE_SCOPE.md).
- **Proof:** dotnet build PASS; dotnet test `BackupRestore|BackendClientExtractionRegression` 16 passed; verify.ps1 -Quick `artifacts/verify/20260323_030840` (authoritative).

**PR-14 — OUT**

- DataBackupService (local backup; different seam).
- UI changes.

**Ownership Sweep (2026-03-23):**

| Search | Result |
|--------|--------|
| `GetBackupsAsync` etc. in IBackendClient.cs | Zero method signatures |
| `GetBackupsAsync` etc. in BackendClient.cs | Zero method definitions |
| `_backend` / IBackendClient in BackupRestoreViewModel.cs | Zero hits; uses _backupRestoreClient only |
| BackupRestoreViewModel | Uses IBackupRestoreClient only; all 7 methods via _backupRestoreClient |
| DataBackupService.CreateBackupAsync, RestoreBackupAsync | Local-file only (BackupResult, RestoreResult); distinct seam, not IBackupRestoreClient |
| IBackupRestoreClient, BackupRestoreClient | Valid ownership; pipeline-based |
| BackupRestoreViewModelSeamTests, BackendClientTransportPolicyTests, BackendClientExtractionRegressionTests | Tests; mock or assert extraction |

Backup domain (7 methods) fully extracted. No backup methods on IBackendClient or BackendClient.

- **Authoritative proof path:** `artifacts/verify/20260323_030840/verification_report.md` (verify.ps1 fix + rerun).

### PR-15 — Models (9 methods) (done 2026-03-23)

**Single extraction:** `GetModelsAsync`, `GetModelAsync`, `RegisterModelAsync`, `VerifyModelAsync`, `UpdateModelChecksumAsync`, `DeleteModelAsync`, `ExportModelAsync`, `ImportModelAsync`, `GetStorageStatsAsync` → `IModelManagerClient` / `ModelManagerClient`. Uses `BackendClientHttpPipeline`.

**Exact caller ownership:** ModelManagerViewModel only. Uses `IModelManagerClient _modelManagerClient`; no `IBackendClient` reference. ModelManagerView, ModelActions use IModelManagerClient via DI.

| Method | Notes |
|--------|-------|
| GetModelsAsync | GET `/api/models` optional `?engine=` |
| GetModelAsync | GET `/api/models/{engine}/{modelName}` |
| RegisterModelAsync | POST `/api/models` JSON body |
| VerifyModelAsync | POST `/api/models/{engine}/{modelName}/verify` |
| UpdateModelChecksumAsync | PUT `/api/models/{engine}/{modelName}/update-checksum` |
| DeleteModelAsync | DELETE `/api/models/{engine}/{modelName}` |
| ExportModelAsync | GET `/api/models/{engine}/{modelName}/export` (stream via GetStreamAsync) |
| ImportModelAsync | POST `/api/models/import` (multipart via PostMultipartAsync) |
| GetStorageStatsAsync | GET `/api/models/stats/storage` |

**PR-15 — IN**

- `ModelManagerClient` uses `BackendClientHttpPipeline` via DI; internal ctor `(pipeline)`.
- AppServices: `services.AddSingleton<IModelManagerClient>(sp => new ModelManagerClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 9 methods removed from `IBackendClient`, `BackendClient`.
- Tests: `GetModelsAsync_ResolvesCorrectPath`, `GetModelAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- Anti-regression: `IBackendClient_DoesNotExposeModelMethods`, `BackendClient_DoesNotExposeModelMethods` in `BackendClientExtractionRegressionTests`.
- Scope: [PR-15_MODELS_SCOPE.md](PR-15_MODELS_SCOPE.md).
- **Proof:** dotnet build PASS; 63 targeted tests; verify.ps1 -Quick `artifacts/verify/20260323_053529` (authoritative).

**PR-15 — OUT**

- TrainingViewModelTests model tests migrated to `IModelManagerClient`; no IBackendClient model mocks.

**Ownership Sweep (2026-03-23):**

| Search | Result |
|--------|--------|
| GetModelsAsync etc. in IBackendClient.cs | Zero method signatures |
| GetModelsAsync etc. in BackendClient.cs | Zero method definitions |
| _backend / IBackendClient model calls in ModelManagerViewModel | Zero hits; uses _modelManagerClient only |
| ModelManagerViewModel | Uses IModelManagerClient only; all 9 methods via _modelManagerClient |
| IModelManagerClient, ModelManagerClient | Valid ownership; pipeline-based |

Model domain (9 methods) fully extracted. No model methods on IBackendClient or BackendClient.

**Next recommended slice after PR-15:** Video or Mixer per [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md).

**What remains in BackendClient (Bucket D):** MCP, Voice, Audio, Projects, Batch, Transcribe, Training, Ensemble, Mixer, Video, Quality, Pipeline (GetPipelineMetricsAsync only). Search, Health/Version, Telemetry, Script editor, Connection status, Macros, Workflows, Effects (chains + presets), Pipeline (providers/process), BackupRestore, and Models are fully extracted.

---

## Post-PR-12 Remainder

See dedicated inventory: [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md).

Summary: Two thin clients (MixerState, VideoGen/VideoEdit) still delegate to IBackendClient. PipelineConversation migrated (PR-13). BackupRestore migrated (PR-14). ModelManager migrated (PR-15). PR-13 done: Pipeline providers/process extracted. PR-14 done: BackupRestore 7 methods extracted. PR-15 done: Models 9 methods extracted.

**Stop criteria:** See [EXTRACTION_STOP_CRITERIA.md](EXTRACTION_STOP_CRITERIA.md) for when not to extract.

---

## Exit criteria

- [x] This inventory committed with PR-1 scope table.
- [x] Policy block moved to `BackendClientHttpPipeline.cs` (verify via diff / type location).
- [x] `BackendClientTransportPolicyTests` green (retry 5xx, 401 no retry, 422 mapping, timeout mapping).
- [x] `dotnet build` App + targeted `dotnet test`; run `verify.ps1 -Quick` before merge.
